using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Midikist.Components;
using Rewired.Utils.Classes.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using ZeepkistClient;
using ZeepkistNetworking;
using ZeepSDK.Cosmetics;
using ZeepSDK.Level;
using ZeepSDK.LevelEditor;
using ZeepSDK.Racing;
using ZeepSDK.Storage;
using ZeepSDK.Workshop;
using Object = UnityEngine.Object;

namespace Midikist
{

    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInDependency("ZeepSDK")]
    public class Plugin : BaseUnityPlugin
    {
        private Harmony harmony;
        public static IModStorage Storage { get; set; }

        private bool IsTesting { get; set; }

        public static ConfigEntry<KeyCode> AddMidiToLevelButton { get; private set; }
        public static ConfigEntry<string> InstrumentConfig { get; private set; }
        public static ConfigEntry<int> NoteMinimum { get; private set; }
        public static ConfigEntry<int> TimeDelay { get; private set; }
        public static ConfigEntry<bool> RemoveOtherBlocks { get; private set; }

        public static ConfigEntry<string> SoundBlockTypeConfig { get; private set; }
        public static ConfigEntry<float> SoundBlockSizeX { get; private set; }
        public static ConfigEntry<float> SoundBlockSizeY { get; private set; }
        public static ConfigEntry<float> SoundBlockSizeZ { get; private set; }
        public static ConfigEntry<float> SoundBlockMoveDown { get; private set; }

        public static LEV_LevelEditorCentral LevelEditorInstance { get; set; }
        public static int SOUND_BLOCK_ID = 2279;

        private void Awake()
        {
            harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            harmony.PatchAll();

            Plugin.Storage = StorageApi.CreateModStorage(this);
            AddMidiToLevelButton = this.Config.Bind<KeyCode>("Mod", "Add Midi Button", KeyCode.F9, new ConfigDescription($"Button to add a midi to the current level.  Will also delete all other noteblocks!"));
            InstrumentConfig = this.Config.Bind<string>("Mod", "Instrument", Instrument.PIANO.ToString(), new ConfigDescription("Instrument to use", new AcceptableValueList<string>(Enum.GetNames(typeof(Instrument)))));
            NoteMinimum = this.Config.Bind<int>("Mod", "MinimumNote", 48, new ConfigDescription("The minimum note to convert from Midi to Zeepkist value."));
            TimeDelay = this.Config.Bind<int>("Mod", "Time Delay", 2000, new ConfigDescription("The delay in milliseconds before adding any notes"));
            RemoveOtherBlocks = this.Config.Bind<bool>("Mod", "Remove Other Sound Blocks", true, new ConfigDescription("Will remove any other sound blocks in the level if set"));

            SoundBlockTypeConfig = this.Config.Bind<string>("Sound Block", "Type", SoundBlockType.Block.ToString(), new ConfigDescription("Soundblock type to use", new AcceptableValueList<string>(Enum.GetNames(typeof(SoundBlockType)))));
            SoundBlockSizeX = this.Config.Bind<float>("Sound Block", "Size X", 1.0F, new ConfigDescription("The X size of the sound block"));
            SoundBlockSizeY = this.Config.Bind<float>("Sound Block", "Size Y", .5F, new ConfigDescription("The Y size of the sound block"));
            SoundBlockSizeZ = this.Config.Bind<float>("Sound Block", "Size Z", .1F, new ConfigDescription("The Z size of the sound block"));
            SoundBlockMoveDown = this.Config.Bind<float>("Sound Block", "Move Up", 10F, new ConfigDescription("Will move the block up this many units.  Will follow rotation so 90 degrees will move left or right."));


            LevelEditorApi.EnteredLevelEditor += LevelEditorApi_EnteredLevelEditor;
            LevelEditorApi.ExitedLevelEditor += LevelEditorApi_ExitedLevelEditor;
            LevelEditorApi.EnteredTestMode += LevelEditorApi_EnteredTestMode;
            LevelEditorApi.LevelLoaded += LevelEditorApi_LevelLoaded;
            LevelEditorApi.LevelSaved += LevelEditorApi_LevelSaved;
            RacingApi.PlayerSpawned += RacingApi_PlayerSpawned;

            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_NAME} is loaded");
        }

        private void LevelEditorApi_EnteredTestMode()
        {
            Logger.LogInfo("Entered test mode");
            this.IsTesting = true;
        }

        private void RacingApi_PlayerSpawned()
        {
            Logger.LogInfo("Player spawned");

            if (!this.IsTesting)
                return;
            GameObject.Find("Soapbox(Clone)").AddComponent<DataRecorder>();
        }

        private void LevelEditorApi_LevelSaved()
        {
            Logger.LogInfo("Level saved");
        }

        private void LevelEditorApi_LevelLoaded()
        {
            Logger.LogInfo("Level loaded");
        }

        private void LevelEditorApi_ExitedLevelEditor()
        {
            Logger.LogInfo("Exited level editor");
            Plugin.LevelEditorInstance = null;
        }

        private void LevelEditorApi_EnteredLevelEditor()
        {
            Logger.LogInfo("Entered level editor");
            this.IsTesting = false;
        }

        private void Update()
        {
            if (Input.GetKeyDown(Plugin.AddMidiToLevelButton.Value))
            {
                Logger.LogInfo("Button pressed!");
                AddMidiToLevel();
            }
        }

        public void AddMidiToLevel()
        {
            Logger.LogInfo("Button to add midi to level just pressed, lets check some things...");
            if (Plugin.LevelEditorInstance == null)
            {
                Logger.LogError("Not in editor, not running mod!");
                return;
            }

            // Load current level
            if (LevelApi.CurrentLevel == null)
            {
                Logger.LogError("Current level not loaded, cannot load the UID!");
                PlayerManager.Instance.messenger.Log("Level not loaded, save first", 1.0f);
                return;
            }
            string uid = LevelApi.CurrentLevel.UID;

            // Load saved points
            if (!Plugin.Storage.JsonFileExists(uid))
            {
                Logger.LogError($"JSON file for uid {uid} does not exist, ensure level was tested!");
                PlayerManager.Instance.messenger.Log("Test level before adding midi", 1.0f);
                return;
            }
            List<Point> points = Plugin.Storage.LoadFromJson<List<Point>>(uid);

            // Load midi file
            string dllFile = System.Reflection.Assembly.GetAssembly(typeof(Plugin)).Location;
            string dllDirectory = Path.GetDirectoryName(dllFile);
            var midiFiles = Directory.GetFiles(dllDirectory, "*.mid");

            if (midiFiles.Length == 0)
            {
                Logger.LogError($"No midi files were found at {dllDirectory}!");
                PlayerManager.Instance.messenger.Log("Midi file not found in plugin directory", 1.0f);
                return;
            }

            if (midiFiles.Length > 1)
            {
                Logger.LogError($"More than 1 midi file found at {dllDirectory}!");
                PlayerManager.Instance.messenger.Log("Too many midi files in plugin directory", 1.0f);
                return;
            }
            var midiFile = midiFiles.First();

            // Delete all existing note blocks
            if (RemoveOtherBlocks.Value)
            {
                var existingSoundBlocks = LevelEditorInstance.undoRedo.allBlocksDictionary.Where(x => x.Value.blockID == SOUND_BLOCK_ID).Select(x => x.Value).ToList();
                Logger.LogInfo($"Deleting existing {existingSoundBlocks.Count()} number of sound blocks");

                List<string> jsonList = LevelEditorInstance.undoRedo.ConvertBlockListToJSONList(existingSoundBlocks);
                List<string> stringList = LevelEditorInstance.undoRedo.ConvertSelectionToStringList(existingSoundBlocks);
                List<string> after = new List<string>();
                List<BlockProperties> theBlocks = new List<BlockProperties>();

                for (int index = 0; index < jsonList.Count; ++index)
                {
                    after.Add((string)null);
                    theBlocks.Add((BlockProperties)null);
                }

                Change_Collection change = LevelEditorInstance.undoRedo.ConvertBeforeAndAfterListToCollection(jsonList, after, theBlocks, stringList, new List<string>());

                foreach (var block in existingSoundBlocks)
                {
                    Object.Destroy((Object)block.gameObject);
                }

                // Force re-validation of level + notify history
                LevelEditorInstance.validation.BreakLock(change, "Midikist");
            }


            // Convert the midi file
            Logger.LogInfo($"Midi file loaded from {midiFile}, adding notes to the map...");
            var midi = MidiFile.Read(midiFile);
            var tempoMap = midi.GetTempoMap();

            // Get the instrument to use
            Instrument instrumentToUse = (Instrument)Enum.Parse(typeof(Instrument), InstrumentConfig.Value);

            // Place the note
            foreach (var note in midi.GetNotes())
            {
                var timespan = TimeConverter.ConvertTo<MetricTimeSpan>(note.Time, tempoMap);
                Logger.LogDebug($"Note: {note.NoteName} - {note.Time} - {timespan}");

                // Find the closest before point
                var lerp = GetLerped(note, tempoMap, points);
                if (lerp == null)
                {
                    Logger.LogError("Unable to find any close points, assuming recorded run is done!");
                    return;
                }
                
                int noteNumber = ConvertNote(note.NoteNumber);
                CreateSoundBlock(lerp.Item1, lerp.Item2, instrumentToUse, noteNumber);
            }
        }

        public int ConvertNote(int noteNumber)
        {
            int minimum = NoteMinimum.Value;
            int maximum = minimum + 23;

            while(noteNumber < minimum)
            {
                noteNumber += 12;
            }

            while(noteNumber > maximum)
            {
                noteNumber -= 12;
            }

            return noteNumber - minimum;
        }

        public Tuple<Vector3, Quaternion> GetLerped(Note note, TempoMap tempoMap, List<Point> points)
        {
            var timespan = TimeConverter.ConvertTo<MetricTimeSpan>(note.Time, tempoMap);
            Logger.LogDebug($"Note: {note.NoteName} - {note.Time} - {timespan}");
            int bufferMs = TimeDelay.Value;

            // Find the closest points
            Point lastPoint = points.First();
            Point currentPoint = null;
            foreach (var point in points)
            {
                if (point.Time > (timespan.TotalMilliseconds + bufferMs))
                {
                    currentPoint = point;
                    break;
                }

                lastPoint = point;
            }

            if (currentPoint == null)
            {
                return null;
            }

            Logger.LogDebug($"Before Point: {lastPoint} - After Point: {currentPoint}");
            var percentage = Math.Abs(lastPoint.Time - bufferMs - timespan.TotalMilliseconds) / Math.Abs(lastPoint.Time - currentPoint.Time);
            var positionLerped = Vector3.Lerp(lastPoint.Position, currentPoint.Position, (float)percentage);
            var rotationLerped = Quaternion.Lerp(lastPoint.Rotation, currentPoint.Rotation, (float)percentage);

            var postionFinal = positionLerped + (rotationLerped * Vector3.up * SoundBlockMoveDown.Value);

            return Tuple.Create(postionFinal, rotationLerped);
        }

        public void CreateSoundBlock(Vector3 position, Quaternion rotation, Instrument instrument, int note)
        {
            float[] options = Enumerable.Repeat<float>(0F, 11).ToArray();
            int soundBlockType = (int)Enum.Parse(typeof(SoundBlockType), SoundBlockTypeConfig.Value);

            options[0 + soundBlockType] = 1;
            options[6] = (float)instrument;
            options[8] = note;

            CreateBlock(SOUND_BLOCK_ID, position, rotation, new Vector3(SoundBlockSizeX.Value, SoundBlockSizeY.Value, SoundBlockSizeZ.Value), null, options);
        }

        public BlockProperties CreateBlock(int blockId, Vector3 position, Quaternion rotation, Vector3 scale, float[] colors = null, float[] options = null)
        {
            if (colors == null)
            {
                colors = Enumerable.Repeat<float>(0F, 16).ToArray();
            }
            if (colors.Length < 16) 
            {
                colors.ToList().AddRange(Enumerable.Repeat<float>(0F, colors.Length - 16));
            }

            if (options == null)
            {
                options = Enumerable.Repeat<float>(0F, 12).ToArray();
            }
            if (options.Length < 12)
            {
                options.ToList().AddRange(Enumerable.Repeat<float>(0F, colors.Length - 12));
            }

            string uniqueIdforBlocks = PlayerManager.Instance.GenerateUniqueIDforBlocks(SOUND_BLOCK_ID.ToString());
            Vector3 eulerAngles = rotation.eulerAngles;
            List<float> properties = new List<float>
                {
                    blockId,
                    position.x, position.y, position.z,
                    eulerAngles.x, eulerAngles.y, eulerAngles.z,
                    scale.x, scale.y, scale.z,

                };
            properties.AddRange(colors);
            properties.AddRange(options);

            BlockPropertyJSON blockProperties = new BlockPropertyJSON()
            {
                blockID = blockId,
                UID = uniqueIdforBlocks,
                position = position,
                localScale = new Vector3(1, 1, .1F),
                eulerAngles = rotation.eulerAngles,
                properties = properties
            };
            var newBlock = LevelEditorInstance.undoRedo.GenerateNewBlock(blockProperties, uniqueIdforBlocks);

            newBlock.transform.position = position;
            newBlock.transform.rotation = rotation;
            newBlock.transform.localScale = scale;
            newBlock.SomethingChanged();

            return newBlock;
        }


        public void OnDestroy()
        {
            harmony?.UnpatchSelf();
            harmony = null;
        }
    }
}
