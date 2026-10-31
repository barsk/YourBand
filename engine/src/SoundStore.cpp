#include "SoundStore.h"

namespace gt
{

namespace
{
    constexpr int version = 2;  // (1: the first try, a set per playstyle: not read)

    SoundSet setFrom (const juce::var& json)
    {
        SoundSet sounds;
        if (auto* instruments = json.getDynamicObject())
            for (const auto& entry : instruments->getProperties())
            {
                Sound sound;
                sound.plugin = entry.value["plugin"].toString();
                if (sound.plugin.isEmpty())
                    continue;
                sound.state.fromBase64Encoding (entry.value["state"].toString());
                sounds[entry.name.toString()] = std::move (sound);
            }
        return sounds;
    }

    juce::var jsonOf (const SoundSet& sounds)
    {
        auto* instruments = new juce::DynamicObject();
        for (const auto& [id, sound] : sounds)
        {
            auto* entry = new juce::DynamicObject();
            entry->setProperty ("plugin", sound.plugin);
            if (sound.state.getSize() > 0)
                entry->setProperty ("state", sound.state.toBase64Encoding());
            instruments->setProperty (id, juce::var (entry));
        }
        return juce::var (instruments);
    }
}

juce::File SoundStore::songFile (const juce::String& song) const
{
    const auto name = song.trim().isEmpty() ? juce::String ("untitled") : song.trim();
    return root.getChildFile ("songs").getChildFile (juce::File::createLegalFileName (name) + ".sounds");
}

SongSounds SoundStore::read (const juce::File& file)
{
    SongSounds sounds;
    if (! file.existsAsFile())
        return sounds;
    const auto json = juce::JSON::parse (file);
    if ((int) json["version"] != version)
        return sounds;
    if (auto* styles = json["styles"].getDynamicObject())
        for (const auto& style : styles->getProperties())
            sounds.styles[style.name.toString()] = setFrom (style.value);
    sounds.tracks = setFrom (json["tracks"]);
    return sounds;
}

bool SoundStore::write (const juce::File& file, const SongSounds& sounds)
{
    auto* styles = new juce::DynamicObject();
    for (const auto& [style, set] : sounds.styles)
        styles->setProperty (style, jsonOf (set));
    auto* top = new juce::DynamicObject();
    top->setProperty ("version", version);
    top->setProperty ("styles", juce::var (styles));
    if (! sounds.tracks.empty())
        top->setProperty ("tracks", jsonOf (sounds.tracks));
    return file.getParentDirectory().createDirectory() && file.replaceWithText (juce::JSON::toString (juce::var (top)));
}

} // namespace gt
