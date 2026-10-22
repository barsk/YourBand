#include <juce_gui_basics/juce_gui_basics.h>

#include "MainComponent.h"

namespace gt
{

class EngineApplication final : public juce::JUCEApplication
{
public:
    const juce::String getApplicationName() override { return JUCE_APPLICATION_NAME_STRING; }
    const juce::String getApplicationVersion() override { return JUCE_APPLICATION_VERSION_STRING; }
    // one program owns the audio interface; a silent test run can go next to it
    bool moreThanOneInstanceAllowed() override { return getCommandLineParameters().contains ("--silent"); }

    void initialise (const juce::String&) override
    {
        // the program is the engine; its window is the control panel
        window = std::make_unique<MainWindow> ("GuitarTutor Control Panel", EngineOptions::fromCommandLine (getCommandLineParameterArray()));
    }
    void shutdown() override { window.reset(); }
    void systemRequestedQuit() override { quit(); }

private:
    class MainWindow final : public juce::DocumentWindow
    {
    public:
        MainWindow (const juce::String& name, const EngineOptions& options)
            : DocumentWindow (name + (options.silent ? " (silent)" : ""), juce::Colours::darkgrey, DocumentWindow::allButtons),
              startedByGame (options.parent > 0)
        {
            setUsingNativeTitleBar (true);
            setContentOwned (new MainComponent (options), true);
            setResizable (true, true);
            centreWithSize (getWidth(), getHeight());
            // started by the game: hidden until the game asks for it
            setVisible (! startedByGame);
        }

        void closeButtonPressed() override
        {
            if (startedByGame)
                setVisible (false);  // the game closes the engine when it closes
            else
                juce::JUCEApplication::getInstance()->systemRequestedQuit();
        }

    private:
        bool startedByGame;
    };

    std::unique_ptr<MainWindow> window;
};

} // namespace gt

START_JUCE_APPLICATION (gt::EngineApplication)
