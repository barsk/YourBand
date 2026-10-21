#include "NumaKeys.h"

#include <cstring>

namespace gt::numa
{

juce::File folder()
{
    return juce::File::getSpecialLocation (juce::File::userApplicationDataDirectory).getChildFile ("Studiologic").getChildFile ("Numa Player");
}

juce::StringArray sounds()
{
    juce::StringArray list;
    for (const auto& lib : folder().getChildFile ("Libraries").findChildFiles (juce::File::findDirectories, false, "*.numalib"))
        for (const auto& sound : lib.getChildFile ("sounds").findChildFiles (juce::File::findFiles, false, "*.numares"))
            list.add (lib.getFileNameWithoutExtension() + "/" + sound.getFileNameWithoutExtension());
    return list;
}

juce::MemoryBlock stateFor (const juce::String& sound, juce::String& error)
{
    const auto parts = juce::StringArray::fromTokens (sound, "/", "");
    if (parts.size() != 2
        || ! folder().getChildFile ("Libraries").getChildFile (parts[0] + ".numalib").getChildFile ("sounds").getChildFile (parts[1] + ".numares").existsAsFile())
    {
        error = "Numa Player has no sound called " + sound;
        return {};
    }

    // the standalone app's last state: a JUCE properties file whose "filterState" is the plugin's own state
    auto settings = juce::parseXML (folder().getChildFile ("Numa Player V2.settings"));
    juce::MemoryBlock state;
    if (settings != nullptr)
        for (auto* value : settings->getChildIterator())
            if (value->getStringAttribute ("name") == "filterState")
                state.fromBase64Encoding (value->getStringAttribute ("val"));
    // JUCE's binary XML: "VC2!", the XML's length (with its trailing zero), the XML, then anything else
    if (state.getSize() < 8 || std::memcmp (state.getData(), "VC2!", 4) != 0)
    {
        error = "no Numa Player state to start from: open the Numa Player app once and choose a sound";
        return {};
    }
    const auto length = (size_t) juce::ByteOrder::littleEndianInt (static_cast<const char*> (state.getData()) + 4);
    if (8 + length > state.getSize())
    {
        error = "the Numa Player state is not readable";
        return {};
    }
    auto xml = juce::String::fromUTF8 (static_cast<const char*> (state.getData()) + 8, (int) length).trimCharactersAtEnd (juce::String::charToString (0));

    // the enabled zone (or the first) gets the sound
    int chosen = -1, first = -1;
    for (int at = xml.indexOf ("<ZONE_STATE "); at >= 0; at = xml.indexOf (at + 1, "<ZONE_STATE "))
    {
        if (first < 0)
            first = at;
        const auto end = xml.indexOf (at, "</ZONE_STATE>");
        const auto zone = xml.substring (at, end < 0 ? xml.length() : end);
        if (chosen < 0 && (zone.contains ("<PARAM id=\"ZONE_ENABLE_ID\" value=\"1\"") || zone.contains ("<PARAM id=\"ZONE_ENABLE_ID\" value=\"1.0\"")))
            chosen = at;
    }
    if (chosen < 0)
        chosen = first;
    const auto attribute = xml.indexOf (chosen, "ZONE_LIBRARY_ID=\"");
    if (chosen < 0 || attribute < 0)
    {
        error = "the Numa Player state has no zone to give a sound";
        return {};
    }
    const auto valueStart = attribute + (int) strlen ("ZONE_LIBRARY_ID=\"");
    const auto valueEnd = xml.indexOf (valueStart, "\"");
    xml = xml.substring (0, valueStart) + sound + xml.substring (valueEnd);

    juce::MemoryBlock component;
    component.append ("VC2!", 4);
    const auto utf8 = xml.toRawUTF8();
    const auto bytes = (juce::uint32) strlen (utf8) + 1;
    const auto le = juce::ByteOrder::swapIfBigEndian (bytes);
    component.append (&le, 4);
    component.append (utf8, bytes);
    const auto rest = 8 + length;
    if (rest < state.getSize())
        component.append (static_cast<const char*> (state.getData()) + rest, state.getSize() - rest);

    // how JUCE's VST3 host stores a plugin's state
    juce::XmlElement wrapper ("VST3PluginState");
    wrapper.createNewChildElement ("IComponent")->addTextElement (component.toBase64Encoding());
    juce::MemoryBlock wrapped;
    juce::AudioProcessor::copyXmlToBinary (wrapper, wrapped);
    return wrapped;
}

} // namespace gt::numa
