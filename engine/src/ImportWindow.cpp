#include "ImportWindow.h"

#include "WindowFront.h"

namespace gt
{

namespace
{
    constexpr int rowHeight = 28;

    /** The window's contents. Runs songtool on a thread of its own and shows what it prints. */
    class ImportPanel final : public juce::Component, private juce::Thread
    {
    public:
        ImportPanel (juce::PropertiesFile& settingsFile, std::function<void (const juce::File&)> importedSong)
            : juce::Thread ("songtool"), settings (settingsFile), imported (std::move (importedSong))
        {
            guitarPro.setToggleState (true, juce::dontSendNotification);
            guitarPro.onClick = [this] { refreshEnabled(); };

            for (auto* label : { &fileLabel, &strumLabel, &chordsLabel, &tabsLabel })
                addAndMakeVisible (*label);
            hint.setColour (juce::Label::textColourId, juce::Colours::grey);
            for (auto* editor : { &fileEdit, &strumEdit, &chordsEdit, &tabsEdit })
            {
                editor->setMultiLine (false);
                addAndMakeVisible (*editor);
            }
            fileEdit.setTextToShowWhenEmpty ("The .gpx, .gp3, .gp4, .gp5 or .gp file", juce::Colours::grey);
            strumEdit.setTextToShowWhenEmpty ("https://tabs.ultimate-guitar.com/tab/... (optional: else the file's strumming)", juce::Colours::grey);
            chordsEdit.setTextToShowWhenEmpty ("https://tabs.ultimate-guitar.com/tab/... (a chords page)", juce::Colours::grey);
            tabsEdit.setTextToShowWhenEmpty ("https://tabs.ultimate-guitar.com/tab/... (a tabs page)", juce::Colours::grey);

            addAndMakeVisible (guitarPro);
            addAndMakeVisible (browse);
            addAndMakeVisible (hint);
            addAndMakeVisible (importButton);
            browse.onClick = [this] { chooseFile(); };
            importButton.onClick = [this] { running() ? cancel() : start(); };

            logView.setMultiLine (true);
            logView.setReadOnly (true);
            logView.setFont (juce::Font (juce::FontOptions (13.0f)));
            addAndMakeVisible (logView);

            refreshEnabled();
            setSize (680, 560);
        }

        ~ImportPanel() override
        {
            cancel();
            stopThread (3000);
        }

        void test (const juce::String& chordsUrl)
        {
            guitarPro.setToggleState (false, juce::dontSendNotification);
            refreshEnabled();
            chordsEdit.setText (chordsUrl);
            start();
        }

        void resized() override
        {
            auto r = getLocalBounds().reduced (12);
            guitarPro.setBounds (r.removeFromTop (rowHeight));
            r.removeFromTop (4);

            auto row = r.removeFromTop (rowHeight);
            fileLabel.setBounds (row.removeFromLeft (150));
            browse.setBounds (row.removeFromRight (100).reduced (4, 0));
            fileEdit.setBounds (row);
            r.removeFromTop (6);
            row = r.removeFromTop (rowHeight);
            strumLabel.setBounds (row.removeFromLeft (150));
            strumEdit.setBounds (row);
            r.removeFromTop (14);

            auto ug = r.removeFromTop (3 * rowHeight + 12);
            row = ug.removeFromTop (rowHeight);
            chordsLabel.setBounds (row.removeFromLeft (150));
            chordsEdit.setBounds (row);
            ug.removeFromTop (6);
            row = ug.removeFromTop (rowHeight);
            tabsLabel.setBounds (row.removeFromLeft (150));
            tabsEdit.setBounds (row);
            hint.setBounds (r.removeFromTop (rowHeight));
            r.removeFromTop (8);

            importButton.setBounds (r.removeFromTop (rowHeight + 4).removeFromLeft (140));
            r.removeFromTop (8);
            logView.setBounds (r);
        }

        void paint (juce::Graphics& g) override
        {
            g.fillAll (getLookAndFeel().findColour (juce::ResizableWindow::backgroundColourId));
        }

    private:
        bool running() { return isThreadRunning(); }

        /** Guitar Pro on: its file and strumming page are for use, else the chords and tabs pages. */
        void refreshEnabled()
        {
            const bool gp = guitarPro.getToggleState();
            for (auto* c : std::initializer_list<juce::Component*> { &fileLabel, &fileEdit, &browse, &strumLabel, &strumEdit })
                c->setEnabled (gp);
            for (auto* c : std::initializer_list<juce::Component*> { &chordsLabel, &chordsEdit, &tabsLabel, &tabsEdit, &hint })
                c->setEnabled (! gp);
            hint.setAlpha (gp ? 0.4f : 1.0f);
        }

        void log (const juce::String& line)
        {
            logView.moveCaretToEnd();
            logView.insertTextAtCaret (line + "\n");
        }

        void chooseFile()
        {
            const auto start = settings.getValue ("importFolder", juce::File::getSpecialLocation (juce::File::userDocumentsDirectory).getFullPathName());
            chooser = std::make_unique<juce::FileChooser> ("A Guitar Pro file", juce::File (start), "*.gpx;*.gp;*.gp3;*.gp4;*.gp5;*.gp7");
            chooser->launchAsync (juce::FileBrowserComponent::openMode | juce::FileBrowserComponent::canSelectFiles,
                                  [safe = juce::Component::SafePointer<ImportPanel> (this)] (const juce::FileChooser& c)
            {
                if (safe == nullptr || c.getResult() == juce::File())
                    return;
                safe->fileEdit.setText (c.getResult().getFullPathName());
                safe->settings.setValue ("importFolder", c.getResult().getParentDirectory().getFullPathName());
                safe->settings.saveIfNeeded();
            });
        }

        void start()
        {
            const auto tool = ImportWindow::findSongtool();
            if (tool == juce::File())
            {
                log ("songtool was not found. Build it once: dotnet build tools/songtool -c Release");
                return;
            }
            juce::StringArray command { tool.getFullPathName(), "import" };
            if (guitarPro.getToggleState())
            {
                const auto file = fileEdit.getText().trim().unquoted();
                if (file.isEmpty() || ! juce::File (file).existsAsFile())
                {
                    log (file.isEmpty() ? "Choose the Guitar Pro file." : "The Guitar Pro file is not there: " + file);
                    return;
                }
                command.add ("--gp");
                command.add (file);
                if (strumEdit.getText().trim().isNotEmpty())
                {
                    command.add ("--strum-url");
                    command.add (strumEdit.getText().trim());
                }
            }
            else
            {
                if (chordsEdit.getText().trim().isEmpty() && tabsEdit.getText().trim().isEmpty())
                {
                    log ("Enter a chords page URL, a tabs page URL, or both: one of them is required.");
                    return;
                }
                if (chordsEdit.getText().trim().isNotEmpty())
                {
                    command.add ("--chords-url");
                    command.add (chordsEdit.getText().trim());
                }
                if (tabsEdit.getText().trim().isNotEmpty())
                {
                    command.add ("--tabs-url");
                    command.add (tabsEdit.getText().trim());
                }
            }

            stopThread (2000);  // (the last run's thread has finished, or is about to)
            logView.clear();
            log ("Importing...");
            cancelled = false;
            saved.clear();
            if (! process.start (command))
            {
                log ("Could not start " + tool.getFullPathName());
                return;
            }
            importButton.setButtonText ("Cancel");
            for (auto* c : std::initializer_list<juce::Component*> { &guitarPro, &fileEdit, &browse, &strumEdit, &chordsEdit, &tabsEdit })
                c->setEnabled (false);
            startThread();
        }

        void cancel()
        {
            if (running())
            {
                cancelled = true;
                process.kill();
            }
        }

        /** On its own thread: reads what songtool prints, line by line, to the end. */
        void run() override
        {
            std::string pending;
            char buffer[4096];
            for (;;)
            {
                const auto n = process.readProcessOutput (buffer, (int) sizeof (buffer));
                if (n > 0)
                {
                    pending.append (buffer, (size_t) n);
                    for (auto newline = pending.find ('\n'); newline != std::string::npos; newline = pending.find ('\n'))
                    {
                        post (juce::String::fromUTF8 (pending.data(), (int) newline).trimEnd());
                        pending.erase (0, newline + 1);
                    }
                }
                else if (! process.isRunning())
                    break;
            }
            if (! pending.empty())
                post (juce::String::fromUTF8 (pending.data(), (int) pending.size()).trimEnd());
            process.waitForProcessToFinish (5000);
            const auto exitCode = (int) process.getExitCode();
            juce::MessageManager::callAsync ([safe = juce::Component::SafePointer<ImportPanel> (this), exitCode]
            {
                if (safe != nullptr)
                    safe->finished (exitCode);
            });
        }

        void post (const juce::String& line)
        {
            juce::MessageManager::callAsync ([safe = juce::Component::SafePointer<ImportPanel> (this), line]
            {
                if (safe == nullptr)
                    return;
                if (line.startsWith ("Saved "))
                    safe->saved = line.fromFirstOccurrenceOf ("Saved ", false, false).trim();
                safe->log (line);
            });
        }

        void finished (int exitCode)
        {
            importButton.setButtonText ("Import");
            guitarPro.setEnabled (true);
            refreshEnabled();
            if (cancelled)
                log ("Cancelled.");
            else if (exitCode == 0 && saved.isNotEmpty())
            {
                log ("Done: " + juce::File (saved).getFileName());
                imported (juce::File (saved));
            }
            else
                log ("The import failed (songtool exit code " + juce::String (exitCode) + ").");
        }

        juce::PropertiesFile& settings;
        std::function<void (const juce::File&)> imported;
        juce::ToggleButton guitarPro { "Guitar Pro (.gpx, .gp3, .gp4, ...)" };
        juce::Label fileLabel { {}, "Guitar Pro file" }, strumLabel { {}, "Strumming (UG URL)" },
            chordsLabel { {}, "Chords page URL" }, tabsLabel { {}, "Tabs page URL" },
            hint { {}, "Ultimate Guitar: at least one of the two is required, both can be used." };
        juce::TextEditor fileEdit, strumEdit, chordsEdit, tabsEdit;
        juce::TextButton browse { "Browse..." }, importButton { "Import" };
        juce::TextEditor logView;
        std::unique_ptr<juce::FileChooser> chooser;
        juce::ChildProcess process;
        juce::String saved;  // the song file songtool says it saved
        std::atomic<bool> cancelled { false };
    };
}

ImportWindow::ImportWindow (juce::PropertiesFile& settings, std::function<void (const juce::File&)> imported)
    : DocumentWindow ("Import a song", juce::Colours::darkgrey, DocumentWindow::closeButton)
{
    setUsingNativeTitleBar (true);
    setContentOwned (new ImportPanel (settings, std::move (imported)), true);
    centreWithSize (getWidth(), getHeight());
    setVisible (true);
    bringToFront (*this);
}

ImportWindow::~ImportWindow() = default;

void ImportWindow::startTest (const juce::String& chordsUrl)
{
    static_cast<ImportPanel*> (getContentComponent())->test (chordsUrl);
}

juce::File ImportWindow::findSongtool()
{
    const auto exe = juce::File::getSpecialLocation (juce::File::currentExecutableFile);
    if (const auto shipped = exe.getSiblingFile ("songtool").getChildFile ("songtool.exe"); shipped.existsAsFile())
        return shipped;
    for (auto dir = exe.getParentDirectory(); dir != dir.getParentDirectory(); dir = dir.getParentDirectory())
        if (const auto built = dir.getChildFile ("tools/songtool/bin/Release/net8.0/songtool.exe"); built.existsAsFile())
            return built;
    return {};
}

} // namespace gt
