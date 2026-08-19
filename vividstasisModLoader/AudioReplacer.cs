using UndertaleModLib;
using UndertaleModLib.Models;

namespace vividstasisModLoader;

public class AudioReplacer(UndertaleData data, string gameDir, string modDir)
{
    readonly string _importFolder = $"{modDir}/audios";

    public bool Exist()
    {
        return Directory.Exists(_importFolder);
    }

    public void Execute()
    {
        if(!Exist()) return;
        UndertaleEmbeddedAudio audioFile = null;
        int audioID = -1;
        int audioGroupID = -1;
        int embAudioID = -1;
        bool usesAGRP = data.AudioGroups.Count > 0;

        string[] dirFiles = Directory.GetFiles(_importFolder);
        string folderName = new DirectoryInfo(_importFolder).Name;

        bool replaceSoundPropertiesCheck = true;

        bool GeneralSound_embedSound = true;
        bool GeneralSound_decodeLoad = false;
        bool GeneralSound_needAGRP = false;
        
        if (GeneralSound_embedSound && data.AudioGroups.Count > 0)
        {
            GeneralSound_needAGRP = false;
        }

        foreach (string file in dirFiles)
        {
            string filename = Path.GetFileName(file);
            if (!(filename.EndsWith(".ogg", StringComparison.InvariantCultureIgnoreCase) || filename.EndsWith(".wav", StringComparison.InvariantCultureIgnoreCase)))
            {
                // Ignore invalid file extensions.
                continue;
            }
            string soundName = Path.GetFileNameWithoutExtension(file);
            bool isOGG = Path.GetExtension(filename).ToLower() == ".ogg";
            bool embedSound = false;
            bool decodeLoad = false;
            if (isOGG)
            {
                embedSound = GeneralSound_embedSound;
                decodeLoad = GeneralSound_decodeLoad;
            }
            else
            {
                // WAV cannot be external
                embedSound = true;
                decodeLoad = false;
            }
            string audioGroupName = "";
            bool needAGRP = false;

            // Search for an existing sound with the given name.
            UndertaleSound existingSound = null;
            for (var i = 0; i < data.Sounds.Count; i++)
            {
                if (data.Sounds[i]?.Name?.Content == soundName)
                {
                    existingSound = data.Sounds[i];
                    break;
                }
            }

            // Try to find an audiogroup, when not updating an existing sound.
            if (embedSound && usesAGRP && existingSound is null)
            {
                needAGRP = GeneralSound_needAGRP;
            }
            if (needAGRP && usesAGRP && embedSound)
            {
                audioGroupName = folderName;

                if (audioGroupID == -1)
                {
                    // Find the audio group we need.
                    for (int i = 0; i < data.AudioGroups.Count; i++)
                    {
                        if (data.AudioGroups[i]?.Name?.Content == audioGroupName)
                        {
                            audioGroupID = i;
                            break;
                        }
                    }
                    if (audioGroupID == -1)
                    {
                        // Still -1? Create a new one...
                        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(gameDir), $"audiogroup{data.AudioGroups.Count}.dat"),
                            Convert.FromBase64String("Rk9STQwAAABBVURPBAAAAAAAAAA="));
                        UndertaleAudioGroup newAudioGroup = new()
                        {
                            Name = data.Strings.MakeString(audioGroupName),
                        };
                        data.AudioGroups.Add(newAudioGroup);
                    }
                }
            }

            // If this is an existing sound, use its audio group ID.
            if (existingSound is not null)
            {
                audioGroupID = existingSound.GroupID;
            }

            // If the audiogroup ID is for the builtin audiogroup ID, it's embedded in the main data file and doesn't need to be loaded.
            if (audioGroupID == data.GetBuiltinSoundGroupID())
            {
                needAGRP = false;
            }

            // Create embedded audio entry if required.
            UndertaleEmbeddedAudio soundData = null;
            if ((embedSound && !needAGRP) || needAGRP)
            {
                soundData = new UndertaleEmbeddedAudio() { Data = File.ReadAllBytes(file) };
                data.EmbeddedAudio.Add(soundData);
                if (existingSound is not null)
                {
                    data.EmbeddedAudio.Remove(existingSound.AudioFile);
                }
                embAudioID = data.EmbeddedAudio.Count - 1;
            }

            // Update external audio group file if required.
            if (needAGRP)
            {
                // Load audiogroup into memory.
                UndertaleData audioGroupDat;
                string relativeAudioGroupPath;
                if (audioGroupID < data.AudioGroups.Count && data.AudioGroups[audioGroupID] is { Path.Content: string customRelativePath })
                {
                    relativeAudioGroupPath = customRelativePath;
                }
                else
                {
                    relativeAudioGroupPath = $"audiogroup{audioGroupID}.dat";
                }
                string audioGroupPath = Path.Combine(Path.GetDirectoryName(gameDir), relativeAudioGroupPath);
                using (FileStream audioGroupReadStream = new(audioGroupPath, FileMode.Open, FileAccess.Read))
                {
                    audioGroupDat = UndertaleIO.Read(audioGroupReadStream);
                }

                // Add the EmbeddedAudio entry to the audiogroup data.
                audioGroupDat.EmbeddedAudio.Add(soundData);
                if (existingSound is not null)
                {
                    audioGroupDat.EmbeddedAudio.Remove(existingSound.AudioFile);
                }
                audioID = audioGroupDat.EmbeddedAudio.Count - 1;

                // Write audio group back to disk.
                using FileStream audioGroupWriteStream = new(audioGroupPath, FileMode.Create);
                UndertaleIO.Write(audioGroupWriteStream, audioGroupDat);
            }

            // Determine sound flags.
            UndertaleSound.AudioEntryFlags flags = UndertaleSound.AudioEntryFlags.Regular;
            if (isOGG && embedSound && decodeLoad)
            {
                // OGG, embed, decode on load.
                flags = UndertaleSound.AudioEntryFlags.IsEmbedded | UndertaleSound.AudioEntryFlags.IsCompressed | UndertaleSound.AudioEntryFlags.Regular;
            }
            if (isOGG && embedSound && !decodeLoad)
            {
                // OGG, embed, not decode on load.
                flags = UndertaleSound.AudioEntryFlags.IsCompressed | UndertaleSound.AudioEntryFlags.Regular;
            }
            if (!isOGG)
            {
                // WAV, always embed.
                flags = UndertaleSound.AudioEntryFlags.IsEmbedded | UndertaleSound.AudioEntryFlags.Regular;
            }
            if (isOGG && !embedSound)
            {
                // OGG, external.
                flags = UndertaleSound.AudioEntryFlags.Regular;
                audioID = -1;
            }

            // Determine final embedded audio reference (or null).
            UndertaleEmbeddedAudio finalAudioReference = null;
            if (!embedSound)
            {
                finalAudioReference = null;
            }
            if (embedSound && !needAGRP)
            {
                finalAudioReference = data.EmbeddedAudio[embAudioID];
            }
            if (embedSound && needAGRP)
            {
                finalAudioReference = null;
            }

            // Determine final audio group reference (or null).
            UndertaleAudioGroup finalGroupReference = null;
            if (!usesAGRP)
            {
                finalGroupReference = null;
            }
            else
            {
                finalGroupReference = needAGRP ? data.AudioGroups[audioGroupID] : data.AudioGroups[data.GetBuiltinSoundGroupID()];
            }

            // Update/create actual sound asset.
            if (existingSound is null)
            {
                UndertaleSound newSound = new()
                {
                    Name = data.Strings.MakeString(soundName),
                    Flags = flags,
                    Type = isOGG ? data.Strings.MakeString(".ogg") : data.Strings.MakeString(".wav"),
                    File = data.Strings.MakeString(filename),
                    Effects = 0,
                    Volume = 1.0f,
                    Pitch = 1.0f,
                    AudioID = audioID,
                    AudioFile = finalAudioReference,
                    AudioGroup = finalGroupReference,
                    GroupID = needAGRP ? audioGroupID : data.GetBuiltinSoundGroupID()
                };
                data.Sounds.Add(newSound);
            }
            else if (replaceSoundPropertiesCheck)
            {
                existingSound.Flags = flags;
                existingSound.Type = isOGG ? data.Strings.MakeString(".ogg") : data.Strings.MakeString(".wav");
                existingSound.File = data.Strings.MakeString(filename);
                existingSound.Effects = 0;
                existingSound.Volume = 1.0f;
                existingSound.Pitch = 1.0f;
                existingSound.AudioID = audioID;
                existingSound.AudioFile = finalAudioReference;
                existingSound.AudioGroup = finalGroupReference;
                existingSound.GroupID = needAGRP ? audioGroupID : data.GetBuiltinSoundGroupID();
            }
            else
            {
                existingSound.AudioFile = finalAudioReference;
                existingSound.AudioID = audioID;
            }
        }
    }
}