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
        window = std::make_unique<MainWindow> (getApplicationName(), EngineOptions::fromCommandLine (getCommandLineParameterArray()));
    }
    void shutdown() override { window.reset(); }
    void systemRequestedQuit() override { quit(); }

private:
    class MainWindow final : public juce::DocumentWindow
    {
    public:
        MainWindow (const juce::String& name, const EngineOptions& options)
            : DocumentWindow (name + (options.silent ? " (silent)" : ""), juce::Colours::darkgrey, DocumentWindow::allButtons)
        {
            setUsingNativeTitleBar (true);
            setContentOwned (new MainComponent (options), true);
            setResizable (true, true);
            centreWithSize (getWidth(), getHeight());
            setVisible (true);
        }

        void closeButtonPressed() override { juce::JUCEApplication::getInstance()->systemRequestedQuit(); }
    };

    std::unique_ptr<MainWindow> window;
};

} // namespace gt

START_JUCE_APPLICATION (gt::EngineApplication)
