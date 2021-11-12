using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BalsaAddons.Steam;
using BalsaCore;
using UnityEngine;
using UnityEngine.Events;
using System.Reflection;

namespace LinuxModSupport
{
    [BalsaAddon]
    public static class Main
    {
        private const string managedFilesPath = "LinuxModSupport.txt";
        private static HashSet<string> managedFiles = new HashSet<string>();
        private static HashSet<string> steamFilesRelative = new HashSet<string>();
        private static List<string> newMods = new List<string>();
        private static string workshopDir = null;
        private static string addonsDir = null;

        [BalsaAddonInit(invokeTime = AddonInvokeTime.MainMenu)]
        public static void Init()
        {
            if (managedFiles == null)
            {
                //Let's make sure we are never called twice.
                return;
            }
            Debug.Log("LinuxModSupport Init");
            workshopDir = SteamworksSetup.Instance.WorkshopDir.Replace('\\', '/');
            addonsDir = Path.Combine(PathUtil.AppRoot, "Addons").Replace('\\', '/');
            LoadManagedFiles();
            SyncFiles();
            if (newMods.Count > 0)
            {
                SaveManagedFiles();
            }
            DisplayMessage();
            managedFiles = null;
        }

        private static void DisplayMessage()
        {
            if (newMods.Count > 0)
            {
                string newModsString;
                StringBuilder sb = new StringBuilder();
                foreach (string newMod in newMods)
                {
                    sb.AppendLine(newMod);
                }
                newModsString = sb.ToString();
                newMods.Clear();
                PopupDialog.Create("Linux Mod Support", $"Please restart the game to load updated mods:\n {newModsString}", IconSprite.Info, new PopupDialogOption("Quit", Application.Quit), new PopupDialogOption("Continue", null));
            }
        }

        private static void LoadManagedFiles()
        {
            managedFiles.Clear();
            if (!File.Exists(managedFilesPath))
            {
                return;
            }
            using (StreamReader sr = new StreamReader(managedFilesPath))
            {
                string currentLine = null;
                while ((currentLine = sr.ReadLine()) != null)
                {
                    managedFiles.Add(currentLine);
                }
            }
        }

        private static void SaveManagedFiles()
        {
            if (File.Exists(managedFilesPath))
            {
                File.Delete(managedFilesPath);
            }
            using (StreamWriter sw = new StreamWriter(managedFilesPath))
            {
                foreach (string managedFile in managedFiles)
                {
                    sw.WriteLine(managedFile);
                }
            }
        }

        private static void SyncFiles()
        {
            //Look at workshop directory and check if there are any changes compared to our cache.
            string[] steamModDirs = Directory.GetDirectories(workshopDir);
            foreach (string steamModDir in steamModDirs)
            {
                string addonID = Path.GetFileName(steamModDir);
                string modTitle = null;
                string modFolderName = null;
                string[] steamFiles = Directory.GetFiles(steamModDir, "*", SearchOption.AllDirectories);
                //Look for and load the modexport.cfg
                foreach (string steamFile in steamFiles)
                {
                    if (steamFile.EndsWith("modexport.cfg", StringComparison.InvariantCultureIgnoreCase))
                    {
                        ConfigNode cn = ConfigNode.Load(steamFile);
                        modTitle = cn.GetValue("Title");
                        modFolderName = cn.GetValue("ModFolderName");
                    }
                }

                //This isn't a plugin, let's continue.
                if (modFolderName == null)
                {
                    continue;
                }

                Debug.Log($"LinuxModSupport plugin {addonID} destination directory is {modFolderName}");

                //Copy workshop mods to addons
                foreach (string modFile in steamFiles)
                {
                    //Stick to using /
                    string modfileReplace = modFile.Replace('\\', '/');

                    //Skip modexport
                    if (modfileReplace.EndsWith("modexport.cfg", StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }

                    //Get paths and create folder for the mods
                    string relativeName = $"{modFolderName}/{modfileReplace.Substring(steamModDir.Length + 1)}";
                    string destination = $"{addonsDir}/{relativeName}";
                    string parentDir = Path.GetDirectoryName(destination);
                    if (!Directory.Exists(parentDir))
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    //Track files that exist so we can check for deleted/unsubscribed mods.
                    if (!steamFilesRelative.Contains(relativeName))
                    {
                        steamFilesRelative.Add(relativeName);
                    }
                    if (!managedFiles.Contains(relativeName))
                    {
                        managedFiles.Add(relativeName);
                    }

                    //If file modified times are the same, skip
                    long sourceTime = Directory.GetLastWriteTime(modfileReplace).Ticks;
                    long destTime = File.Exists(destination) ? File.GetLastWriteTime(destination).Ticks : 0;
                    if (sourceTime == destTime)
                    {
                        continue;
                    }

                    //Copy!
                    File.Copy(modFile, destination, true);

                    //Report a changed mod
                    if (!newMods.Contains(modTitle))
                    {
                        newMods.Add(modTitle);
                    }
                    if (!managedFiles.Contains(relativeName))
                    {
                        managedFiles.Add(relativeName);
                    }
                    Debug.Log($"Updated {relativeName}, {modfileReplace} => {destination}");
                }
            }

            List<string> deleteList = new List<string>();
            foreach (string managedFile in managedFiles)
            {
                if (!steamFilesRelative.Contains(managedFile))
                {
                    string deleteFile = $"{addonsDir}/{managedFile}";
                    if (deleteFile.EndsWith(".modcfg", StringComparison.InvariantCultureIgnoreCase))
                    {
                        ConfigNode cn = ConfigNode.Load(deleteFile);
                        ConfigNode cn2 = cn.GetNode("ModCFG");
                        string modTitle = cn2.GetValue("title");
                        if (!string.IsNullOrEmpty(modTitle))
                        {
                            newMods.Add(modTitle);
                        }
                    }
                    deleteList.Add(managedFile);
                    File.Delete(deleteFile);
                    Debug.Log($"LinuxModSupport deleted {deleteFile}");
                }
            }

            foreach (string deleteString in deleteList)
            {
                managedFiles.Remove(deleteString);
            }
        }
    }
}
