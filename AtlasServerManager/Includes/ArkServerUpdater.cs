﻿using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace AtlasServerManager.Includes
{
    class ArkServerUpdater
    {
        private static bool UpdateError = false, FirstLaunch = true;
        private static string DownloadSizeString;
        private static ulong DownloadSizeBytes = 0;

        public static string GenerateUpdateMessage(AtlasServerManager ArkMgr, int SleepTime, string time = "Minutes")
        {
            return ArkMgr.ServerUpdateMessage.Text.Replace("{time}", SleepTime.ToString() + " " + time);
        }

        public static void CheckForUpdates(object Data)
        {
            AtlasServerManager ArkMgr = (AtlasServerManager)Data;
            if (ArkMgr.checkAutoServerUpdate.Checked || ArkMgr.ForcedUpdate) ArkMgr.Log("[Atlas] Checking for updates, can take up to 30 seconds...");
            string CurrentVersion = "";
            int UpdateVersion = 0, CurrentVer = 0;
            while (true)
            {
                if (ArkMgr.checkAutoServerUpdate.Checked || ArkMgr.ForcedUpdate)
                {
                    UpdateVersion = GetAtlasServerBuildID(ArkMgr);
                    if (UpdateVersion != 0)
                    {
                        if (File.Exists(ArkMgr.SteamPath + "AtlasLatestVersion.txt")) using (StreamReader r = new StreamReader(ArkMgr.SteamPath + "AtlasLatestVersion.txt")) CurrentVersion = r.ReadLine();
                        int.TryParse(CurrentVersion, out CurrentVer);
                        bool ServerStillOpen = false;
                        if (CurrentVer != UpdateVersion)
                        {
                            ArkMgr.Updating = true;
                            ArkMgr.Log("[Atlas] BuildID " + UpdateVersion + " Released!");
                            foreach (ArkServerListViewItem ASLVI in ArkMgr.ServerList.Items)
                                if (ASLVI.GetServerData().IsRunning()) ServerStillOpen = true;
                            if (ServerStillOpen)
                            {
                                int SleepTime = (int)ArkMgr.numServerWarning.Value / 2;
                                ArkMgr.Log("[Atlas] Update Broadcasting " + (int)ArkMgr.numServerWarning.Value + " Minutes");
                                SourceRconTools.SendCommandToAll("broadcast " + GenerateUpdateMessage(ArkMgr, (int)ArkMgr.numServerWarning.Value));

                                Thread.Sleep(SleepTime * 60000);

                                ArkMgr.Log("[Atlas] Update Broadcasting " + SleepTime + " Minutes");
                                SourceRconTools.SendCommandToAll("broadcast " + GenerateUpdateMessage(ArkMgr, SleepTime));                                
                                SleepTime = (int)ArkMgr.numServerWarning.Value / 4;
                                Thread.Sleep(SleepTime * 60000);

                                ArkMgr.Log("[Atlas] Update Broadcasting " + SleepTime + " Minutes");
                                SourceRconTools.SendCommandToAll("broadcast " + GenerateUpdateMessage(ArkMgr, SleepTime));
                                SleepTime = (int)ArkMgr.numServerWarning.Value / 4;
                                Thread.Sleep((SleepTime * 60000) - 35000);

                                ArkMgr.Log("[Atlas] Update Broadcasting 30 Seconds");
                                SourceRconTools.SendCommandToAll("broadcast " + GenerateUpdateMessage(ArkMgr, 30, "Seconds"));
                                Thread.Sleep(30000);
                                ArkMgr.Log("[Atlas] Update Saving World");
                                SourceRconTools.SendCommandToAll("broadcast " + ArkMgr.ServerUpdatingMessage.Text);
                                Thread.Sleep(5000);
                                if (!SourceRconTools.SaveWorld())
                                {
                                    ArkMgr.Log("[Atlas] Failed Saving World, Not Updating!");
                                    continue;
                                }
                            }

                            while (ServerStillOpen)
                            {
                                ServerStillOpen = false;
                                foreach (ArkServerListViewItem ASLVI in ArkMgr.ServerList.Items)
                                    if (ASLVI.GetServerData().IsRunning()) ServerStillOpen = true;
                                Thread.Sleep(3000);
                                if (!ServerStillOpen) break;
                            }
                            ArkMgr.Log("[Atlas] Current BuildID: " + CurrentVersion + ", Updating To BuildID: " + UpdateVersion);
                            UpdateArk(ArkMgr, UpdateVersion.ToString());
                            if (UpdateError)
                            {
                                ArkMgr.Log("[Atlas] Update Error, Retrying...");
                                UpdateError = false;
                                continue;
                            }
                            ArkMgr.Log("[Atlas] Updated, Launching Servers!");
                            FirstLaunch = true;
                            ArkMgr.ForcedUpdate = ArkMgr.Updating = false;
                            StartServers(ArkMgr);
                        }
                        else ArkMgr.Updating = false;
                    }
                }
                else ArkMgr.Updating = false;
                if(!FirstLaunch)
                {
                    ArkMgr.Log("[Atlas] No updates found, Booting servers!");
                    FirstLaunch = false;
                    StartServers(ArkMgr);
                }
                Thread.Sleep((int)(ArkMgr.numServerUpdate.Value * 60000));
            }
        }

        private static int GetAtlasServerBuildID(AtlasServerManager ArkMgr)
        {
            try
            {
                if (!Directory.Exists(ArkMgr.SteamPath)) Directory.CreateDirectory(ArkMgr.SteamPath);
                if (!File.Exists(ArkMgr.SteamPath + "steamcmd.exe")) File.WriteAllBytes(ArkMgr.SteamPath + "steamcmd.exe", Properties.Resources.steamcmd);
                ArkMgr.UpdateProcess = new Process()
                {
                    StartInfo = new ProcessStartInfo(ArkMgr.SteamPath + "steamcmd.exe", "+@NoPromptForPassword 1 +@sSteamCmdForcePlatformType windows +login anonymous +app_info_update 1 +app_info_print 1006030 +app_info_print 1006030 +app_info_print 1006030 +quit")
                    {
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        WorkingDirectory = ArkMgr.SteamPath
                    }
                };
                ArkMgr.UpdateProcess.Start();
                string output = ArkMgr.UpdateProcess.StandardOutput.ReadToEnd();
                ArkMgr.UpdateProcess.WaitForExit();
                int Version = 0;
                int BuildIndex = output.IndexOf("\"buildid\"		\"");
                if(BuildIndex != -1)
                {
                    BuildIndex += 12;
                    int EndIndex = output.IndexOf('"', BuildIndex) - BuildIndex;
                    if (EndIndex != -1) int.TryParse(output.Substring(BuildIndex, EndIndex), out Version);
                }
                return Version;
            }
            catch { ArkMgr.Log("[Update] Failed Checking For Updates..."); }     
            return 0;
        }

        private static List<string> UpdatePaths = new List<string>();
        private static void UpdateArk(AtlasServerManager ArkMgr, string UpdateVersion)
        {
            ArkMgr.FirstDl = false;
            ArkMgr.Invoke((System.Windows.Forms.MethodInvoker)delegate ()
            {
                foreach (ArkServerListViewItem ASLVI in ArkMgr.ServerList.Items)
                {
                    string UpdatePath = ASLVI.GetServerData().ServerPath;
                    if (UpdatePath.StartsWith("./") || UpdatePath.StartsWith(@".\")) UpdatePath = UpdatePath.Replace("./", System.AppDomain.CurrentDomain.BaseDirectory).Replace(@".\", System.AppDomain.CurrentDomain.BaseDirectory).Replace("//", "/").Replace(@"\\", @"\");
                    
                    if (!Directory.Exists(Path.GetDirectoryName(UpdatePath))) Directory.CreateDirectory(Path.GetDirectoryName(UpdatePath));

                    if (UpdatePath.Contains(@"ShooterGame\Binaries\Win64")) UpdatePath = Regex.Split(UpdatePath, "\\ShooterGame")[0];

                    if (!UpdatePaths.Contains(UpdatePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\\'))) UpdatePaths.Add(UpdatePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '\\'));
                }
            });
            
            foreach (string UpdatePath in UpdatePaths)
            {
                ArkMgr.Log("[Ark] Updating Path: " + UpdatePath);
                ArkMgr.UpdateProcess = new Process()
                {
                    StartInfo = new ProcessStartInfo(ArkMgr.SteamPath + "steamcmd.exe", "+@NoPromptForPassword 1 +@sSteamCmdForcePlatformType windows +login anonymous +force_install_dir \"" + UpdatePath + "\" +app_update 1006030 validate +quit")
                    {
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true,
                        WorkingDirectory = ArkMgr.SteamPath
                    }
                };
                ArkMgr.UpdateProcess.OutputDataReceived += (s, e) => UpdateData(e.Data);
                ArkMgr.UpdateProcess.Start();
                ArkMgr.UpdateProcess.BeginOutputReadLine();
                ArkMgr.UpdateProcess.WaitForExit();
            }
            if (!UpdateError) using (StreamWriter w = new StreamWriter(ArkMgr.SteamPath + "AtlasLatestVersion.txt")) w.WriteLine(UpdateVersion);
        }

        private static void StartServers(AtlasServerManager ArkMgr)
        {
            ArkMgr.Invoke((System.Windows.Forms.MethodInvoker)delegate ()
            {
                foreach (ArkServerListViewItem ASLVI in ArkMgr.ServerList.Items)
                    ASLVI.GetServerData().StartServer();
            });
        }

        private static void UpdateData(string input)
        {
            if (input != null && input.Length > 1)
            {
                if (input.Contains("Error! App "))
                {
                    UpdateError = true;
                    if(input.Contains("606") || input.Contains("602"))
                    {
                        AtlasServerManager.GetInstance().Log("[Update] Attempting to fix update error!");
                        try
                        {
                            Directory.Delete(AtlasServerManager.GetInstance().SteamPath + @"steamapps\", true);
                            Directory.Delete(AtlasServerManager.GetInstance().SteamPath + @"steamapps\", true);
                        }
                        catch
                        { }
                    }
                }
                if (input.Contains("progress: "))
                {
                    string PercentText = Regex.Split(input, "progress: ")[1];
                    PercentText = PercentText.Substring(0, PercentText.IndexOf(' '));
                    string[] splts = input.Split('(');
                    if (splts.Length == 3 && splts[2].Contains("/"))
                    {
                        splts = Regex.Split(splts[2].Replace(")", ""), " / ");
                        if (splts.Length == 2)
                        {
                            if (splts[0] != "0" && splts[1] != "0")
                            {
                                DownloadSizeBytes = ulong.Parse(splts[1]);
                                DownloadSizeString = FormatBytes((long)DownloadSizeBytes);
                                input = input.Replace(splts[0], FormatBytes(long.Parse(splts[0]))).Replace(splts[1], DownloadSizeString);
                            }
                            else if (splts[1] != "0")
                            {
                                DownloadSizeBytes = ulong.Parse(splts[1]);
                                DownloadSizeString = FormatBytes((long)DownloadSizeBytes);
                                input = input.Replace(splts[1], DownloadSizeString);
                            }
                        }
                    }
                }
                AtlasServerManager.GetInstance().Log("[Update]" + input);
            }
        }

        private static string FormatBytes(System.Int64 bytes)
        {

            if (bytes >= 0x40000000) return string.Format("{0:F2} GB", (double)(bytes >> 20) / 1024);
            if (bytes >= 0x100000) return string.Format("{0:F2} MB", (double)(bytes >> 10) / 1024);
            if (bytes >= 0x400) return string.Format("{0:F2} KB", (double)bytes / 1024);
            return string.Format("{0:F2} Bytes", bytes);
        }
    }
}