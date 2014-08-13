using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MahApps.Metro.Controls;
using System.Net;
using Codeplex.Data;
using System.Data;
using System.ComponentModel;
using SteamMasterServer.Lib;
using System.Text.RegularExpressions;
using System.Diagnostics;

// A lightweight replacement for Custom Combat Gaming's Arma2 mod launcher
// CCG's Arma2 launcher doesn't work so well for me. Often does not return online status or player counts
// Also, just generally don't like the design. Sorry guys
// Someday I might manage downloading mods with this, but DayZ Commander has it down pat

// My C# knowledge is slightly more than by ability to Google. Please excuse the mess below.

// Zack Boehn 2014
// MahApps Metro WPF Library - https://github.com/MahApps/MahApps.Metro
// DyanmicJSON Library - https://dynamicjson.codeplex.com/
// Source Query Library by awillinger-tgm - https://github.com/awillinger-tgm/SourceServerQuery
// Icon by icons8 - http://www.icons8.com

namespace ServersWPF
{
    public class Server
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string FullIP { get; set; }
        public string IP { get; set; }
        public string Port { get; set; }
        public string SPort { get; set; }
        public string Players { get; set; }
        public string Mods { get; set; }

    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public MainWindow()
        {
            InitializeComponent();

            // Grab CCG's server listing file
            WebClient webClient = new WebClient();
            dynamic result = DynamicJson.Parse(webClient.DownloadString("http://ccg.launcher.eu/launcher/servers.php"));

            int i=0;
            foreach (var server in result.servers)
            {
                //if (!server.name.StartsWith("--"))
                //{
                    string modlist = "";
                    foreach (var mod in server.mods) {
                        // CCG names Epoch by version, while DayZ Commander names it "DayZ_Epoch"
                        // Might add a setting toggle for this
                        if (mod == "Dayz_Epoch1051" && Properties.Settings.Default.dayzcomepoch) { modlist += "@Dayz_Epoch;"; }
                        else { 
                            modlist += "@" + mod + ";";
                        }
                    }
                    // Add each server to the list. TODO: use collection? Group servers by heading.
                    // CCG's JSON is not laid out well - instead of actually grouping servers, "groups" begin with a fake server to add headings
                    ServerGrid.Items.Add(new Server() { ID = i, Name = server.name, Players = server.players, FullIP = server.ip + ":" + server.port, IP = server.ip, Port = server.port, SPort = server.steamport, Mods = modlist });
                    i++;
                //}
            }

            // Grab settings and populate the settings pane with them
            setting_arma2.Text = Properties.Settings.Default.arma2;
            setting_arma2oa.Text = Properties.Settings.Default.arma2oa;
            setting_mods.Text = Properties.Settings.Default.mods;
            settings_params.Text = Properties.Settings.Default.launchparams;
            settings_dayzcomepoch.IsChecked = Properties.Settings.Default.dayzcomepoch;
        }

        // MouseUp action on a server row
        private void ServerGrid_Click(object sender, EventArgs e)
        {
            Server server = (Server)ServerGrid.SelectedItem;

            // Don't want to try to query the fake servers CCG uses as group headings
            if (!server.Name.StartsWith("---"))
            {
                int port = Convert.ToInt32(server.SPort);

                // Arma2 servers respond to Source server queries
                SourceServerQuery query = new SourceServerQuery(server.IP, port);
                ServerInfoResponse sr = query.GetServerInformation();
                PlayersResponse pr = query.GetPlayerList();

                // Slide out details pane
                serverDetails.IsOpen = true;

                // If the server responds with a name, let's go
                // Actually I don't think this works - the source query lib returns "N/A (Timed out)" instead of null
                if (sr.name != null)
                {

                    // Populate the details pane with, ya know, details
                    serverDetails.Header = server.Name;
                    fly_serverName.Content = stripUni(sr.name);
                    fly_serverName.ToolTip = stripUni(sr.name);
                    fly_serverPlayers.Content = "Players: " + sr.players.ToString() + "/" + sr.maxplayers.ToString();
                    fly_serverIP.Content = server.FullIP;
                    fly_serverVer.Content = "v" + stripUni(sr.version);
                    fly_serverGame.Content = "Gamemode: "+stripUni(sr.game);
                    if (server.Mods != "")
                    {
                        fly_serverMods.Content = "Mods: " + server.Mods;
                        fly_serverMods.ToolTip = server.Mods;
                    }
                    else { fly_serverMods.Visibility = System.Windows.Visibility.Hidden; }

                    // Don't need to show a player list for an empty server
                    if (sr.players > 0)
                    {
                        // Modified SourceQuery Library for larger buffer size.
                        // Previously returned null (SocketException) for >~ 51 players
                        try
                        {
                            fly_playerList.Items.Clear();
                            fly_playerList.Visibility = System.Windows.Visibility.Visible;
                            foreach (var player in pr.players) { fly_playerList.Items.Add(stripUni(player.name)); }
                        }
                        catch (NullReferenceException ex)
                        {
                            fly_serverPlayers.Content += " (Error Reading Players)";
                            fly_playerList.Visibility = System.Windows.Visibility.Hidden;
                        }
                    }
                    else { fly_playerList.Visibility = System.Windows.Visibility.Hidden; }

                }

                // Again, this probably won't work because query doesn't return null if server doesn't respond
                else
                {
                    fly_serverName.Content = "Error querying server, possibly offline.";
                }

            }
        }

        // Launch the server - via server row double click or the join button
        private void Launch(object sender, EventArgs e)
        {
            Server server = (Server)ServerGrid.SelectedItem;

            // Split my shitty way of passing mods from CCG's JSON
            string[] mods = server.Mods.Split(new string[] { ";" }, StringSplitOptions.None);

            string modparams = "";

            int i = 0;

            while( i <= (mods.Count() - 1))
            {
                foreach(var mod in mods){
                    if (mod != "") { modparams += Properties.Settings.Default.mods + "\\" + mods[i]+";"; }
                    i++;
                }
            }

            // Prep for launch, grabbing user path settings, server details, and mods
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = @Properties.Settings.Default.arma2oa+"\\ArmA2OA.exe"; // Launch with BattleEye
            startInfo.Arguments = @"-connect="+server.IP+" -port="+server.Port+" \"-mod="+Properties.Settings.Default.arma2+";Expansion;"+modparams+"\" -world=empty "+Properties.Settings.Default.launchparams;
            // Let's GO!
            try
            {
                Process.Start(startInfo);
            }
            // TODO: more detailed error messages
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show("Could not find Arma 2 OA executable.\nPlease ensure the paths are correct and\nthere are no trailing slashes.", "Launch Error");
            }
        }
        
        // Copy IP to clipboard
        private void CopyIP(object Sender, EventArgs e)
        {
            Server server = (Server)ServerGrid.SelectedItem;
            Clipboard.SetText(server.FullIP);
        }
        
        // Open the settings pane
        private void Settings_Click(object Sender, EventArgs e)
        {
            settings.IsOpen = true;
        }

        // Save user settings
        private void SaveSettings(object Sender, EventArgs e)
        {
            Properties.Settings.Default.arma2 = setting_arma2.Text;
            Properties.Settings.Default.arma2oa = setting_arma2oa.Text;
            Properties.Settings.Default.mods = setting_mods.Text;
            Properties.Settings.Default.launchparams = settings_params.Text;
            Properties.Settings.Default.dayzcomepoch = settings_dayzcomepoch.IsChecked ?? false;
            Properties.Settings.Default.Save();

            settings.IsOpen = false;
        }

        // Remove unicode characters from a string.
        // TODO: fix this in the source query library
        public string stripUni(string text)
        {
            text = Regex.Replace(text, @"[^\u001F-\u007F]", string.Empty);
            return text;
        }

    }



}