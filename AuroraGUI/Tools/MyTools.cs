﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Win32;
using MojoUnity;

namespace LeafDNS.Tools
{
    static class MyTools
    {
        public static void BgwLog(string log)
        {
            using (BackgroundWorker worker = new BackgroundWorker())
            {
                worker.DoWork += (o, ea) =>
                {
                    if (!Directory.Exists("Log"))
                        Directory.CreateDirectory("Log");
                    File.AppendAllText($"{MainWindow.SetupBasePath}Log/{DateTime.Today.Year}{DateTime.Today.Month:00}{DateTime.Today.Day:00}.log", log + Environment.NewLine);
                };

                worker.RunWorkerAsync();
            }
        }

        public static bool PortIsUse(int port)
        {
            IPEndPoint[] ipEndPointsTcp = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            IPEndPoint[] ipEndPointsUdp = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners();

            return ipEndPointsTcp.Any(endPoint => endPoint.Port == port)
                   || ipEndPointsUdp.Any(endPoint => endPoint.Port == port);
        }

        public static void SetRunWithStart(bool started, string name, string path)
        {
            RegistryKey reg = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            if (started)
                reg.SetValue(name, path);
            else
                reg.DeleteValue(name);
        }

        public static bool GetRunWithStart(string name)
        {
            RegistryKey reg = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
            try
            {
                return !string.IsNullOrWhiteSpace(reg.GetValue(name).ToString());
            }
            catch
            {
                return false;
            }
        }

        public static bool IsNslookupLocDns()
        {
            var process = Process.Start(new ProcessStartInfo("nslookup.exe", "sjtu.edu.cn")
                {UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true});
            process.WaitForExit();
            return process.StandardOutput.ReadToEnd().Contains("127.0.0.1");
        }

        public static void CheckUpdate(string filePath)
        {
            try
            {
                var jsonStr = Regex.Replace(Encoding.UTF8.GetString(new WebClient { Headers = { ["User-Agent"] = "AuroraDNSC/0.1" } }.DownloadData(
                    "https://api.github.com/repos/mili-tan/AuroraDNS.GUI/releases/latest")), @"[\u4e00-\u9fa5|\u3002|\uff0c]", "");
                var assets = Json.Parse(jsonStr).AsObjectGetArray("assets");
                var fileTime = File.GetLastWriteTime(filePath);
                string downloadUrl = assets[0].AsObjectGetString("browser_download_url");

                if (Convert.ToInt32(downloadUrl.Split('/')[7]) >
                    Convert.ToInt32(fileTime.Year - 2000 + fileTime.Month.ToString("00") + fileTime.Day.ToString("00")))
                    Process.Start(downloadUrl);
                else
                    MessageBox.Show($"当前AuroraDNS.GUI({DateTime.Today.Year - 2000}{DateTime.Today.Month:00}{DateTime.Today.Day:00})已是最新版本,无需更新。");
            }
            catch (Exception e)
            {
                BgwLog(@"| Download list failed : " + e);
            }
        }

        // ReSharper disable once UnusedMember.Global
        public static string IsoCountryCodeToFlagEmoji(string country)
        {
            return string.Concat(country.ToUpper().Select(x => char.ConvertFromUtf32(x + 0x1F1A5)));
        }

        public class MWebClient : WebClient
        {
            public bool AllowAutoRedirect { get; set; } = false;
            protected override WebRequest GetWebRequest(Uri address)
            {
                var request = base.GetWebRequest(address);
                if (request is HttpWebRequest webRequest)
                    webRequest.AllowAutoRedirect = AllowAutoRedirect;
                return request;
            }
        }
    }
}
