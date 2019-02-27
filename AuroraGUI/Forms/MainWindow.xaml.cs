﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ARSoft.Tools.Net.Dns;
using LeafDNS.DnsSvr;
using LeafDNS.Fx;
using LeafDNS.Tools;
using MaterialDesignThemes.Wpf;
using WinFormMenuItem = System.Windows.Forms.MenuItem;
using WinFormContextMenu = System.Windows.Forms.ContextMenu;
using static System.AppDomain;
using MessageBox = System.Windows.MessageBox;

// ReSharper disable NotAccessedField.Local

namespace LeafDNS
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow
    {
        public static string SetupBasePath = CurrentDomain.SetupInformation.ApplicationBase;
        public static IPAddress IntIPAddr;
        public static IPAddress LocIPAddr;
        public static NotifyIcon NotifyIcon;
        private DnsServer MDnsServer;
        private BackgroundWorker MDnsSvrWorker = new BackgroundWorker(){WorkerSupportsCancellation = true};

        public MainWindow()
        {
            InitializeComponent();

            WindowStyle = WindowStyle.SingleBorderWindow;

            if (TimeZoneInfo.Local.Id.Contains("China Standard Time") && RegionInfo.CurrentRegion.GeoId == 45) 
            {
                //Mainland China PRC
                DnsSettings.SecondDnsIp = IPAddress.Parse("119.29.29.29");
                DnsSettings.HttpsDnsUrl = "https://neatdns.ustclug.org/resolve";
                UrlSettings.MDnsList = "https://cdn.jsdelivr.net/gh/AuroraDNS/AuroraDNS.github.io/Localization/DNS-CN.list";
                UrlSettings.WhatMyIpApi = "https://myip.ustclug.org/";
            }
            else if (TimeZoneInfo.Local.Id.Contains("Taipei Standard Time") && RegionInfo.CurrentRegion.GeoId == 237) 
            {
                //Taiwan ROC
                DnsSettings.SecondDnsIp = IPAddress.Parse("101.101.101.101");
                DnsSettings.HttpsDnsUrl = "https://dns.twnic.tw/dns-query";
                UrlSettings.MDnsList = "https://cdn.jsdelivr.net/gh/AuroraDNS/AuroraDNS.github.io/Localization/DNS-TW.list";
            }
            else if (RegionInfo.CurrentRegion.GeoId == 104)
                //HongKong SAR
                UrlSettings.MDnsList = "https://cdn.jsdelivr.net/gh/AuroraDNS/AuroraDNS.github.io/Localization/DNS-HK.list";

            if (File.Exists($"{SetupBasePath}url.json"))
                UrlSettings.ReadConfig($"{SetupBasePath}url.json");

            if (File.Exists($"{SetupBasePath}config.json"))
                DnsSettings.ReadConfig($"{SetupBasePath}config.json");

            if (DnsSettings.BlackListEnable && File.Exists($"{SetupBasePath}black.list"))
                DnsSettings.ReadBlackList($"{SetupBasePath}black.list");

            if (DnsSettings.WhiteListEnable && File.Exists($"{SetupBasePath}white.list"))
                DnsSettings.ReadWhiteList($"{SetupBasePath}white.list");

            if (DnsSettings.WhiteListEnable && File.Exists($"{SetupBasePath}rewrite.list"))
                DnsSettings.ReadWhiteList($"{SetupBasePath}rewrite.list");

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

//            if (false)
//                ServicePointManager.ServerCertificateValidationCallback +=
//                    (sender, cert, chain, sslPolicyErrors) => true;
//                //强烈不建议 忽略 TLS 证书安全错误
//
//            switch (0.0)
//            {
//                case 1:
//                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
//                    break;
//                case 1.1:
//                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls11;
//                    break;
//                case 1.2:
//                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
//                    break;
//                default:
//                    ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
//                    break;
//            }

            LocIPAddr = IPAddress.Parse(IpTools.GetLocIp());
            IntIPAddr = IPAddress.Parse(IpTools.GetIntIp());

            MDnsServer = new DnsServer(DnsSettings.ListenIp, 10, 10);
            MDnsServer.QueryReceived += QueryResolve.ServerOnQueryReceived;
            MDnsSvrWorker.DoWork += (sender, args) => MDnsServer.Start();
            MDnsSvrWorker.Disposed += (sender, args) => MDnsServer.Stop();
            
            NotifyIcon = new NotifyIcon(){Text = @"AuroraDNS",Visible = false,
                Icon = Properties.Resources.AuroraWhite};
            WinFormMenuItem showItem = new WinFormMenuItem("最小化 / 恢复", MinimizedNormal);
            WinFormMenuItem restartItem = new WinFormMenuItem("重新启动", (sender, args) =>
            {
                if (MDnsSvrWorker.IsBusy)
                    MDnsSvrWorker.Dispose();
                Process.Start(new ProcessStartInfo {FileName = GetType().Assembly.Location});
                Environment.Exit(Environment.ExitCode);
            });
            WinFormMenuItem notepadLogItem = new WinFormMenuItem("查阅日志", (sender, args) =>
            {
                if (File.Exists(
                    $"{SetupBasePath}Log/{DateTime.Today.Year}{DateTime.Today.Month:00}{DateTime.Today.Day:00}.log"))
                    Process.Start(new ProcessStartInfo("notepad.exe",
                        $"{SetupBasePath}Log/{DateTime.Today.Year}{DateTime.Today.Month:00}{DateTime.Today.Day:00}.log"));
                else
                    MessageBox.Show("找不到当前日志文件，或当前未产生日志文件。");
            });
            WinFormMenuItem abootItem = new WinFormMenuItem("关于…", (sender, args) => new AboutWindow().ShowDialog());
            WinFormMenuItem updateItem = new WinFormMenuItem("检查更新…", (sender, args) => MyTools.CheckUpdate(GetType().Assembly.Location));
            WinFormMenuItem settingsItem = new WinFormMenuItem("设置…", (sender, args) => new SettingsWindow().ShowDialog());
            WinFormMenuItem exitItem = new WinFormMenuItem("退出", (sender, args) => Environment.Exit(Environment.ExitCode));

            NotifyIcon.ContextMenu =
                new WinFormContextMenu(new[]
                {
                    showItem, notepadLogItem, new WinFormMenuItem("-"), abootItem, updateItem, settingsItem, new WinFormMenuItem("-"), restartItem, exitItem
                });

            NotifyIcon.DoubleClick += MinimizedNormal;

            if (MyTools.IsNslookupLocDns())
                IsSysDns.ToolTip = "已设为系统 DNS";
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Hidden;
            WindowBlur.SetEnabled(this, true);
            var desktopWorkingArea = SystemParameters.WorkArea;
            Left = desktopWorkingArea.Right - Width - 1;
            Top = desktopWorkingArea.Bottom - Height - 0;

            FadeIn(0.50);
            Visibility = Visibility.Visible;
            NotifyIcon.Visible = true;

            if (!MyTools.PortIsUse(53))
            {
                IsLog.IsChecked = DnsSettings.DebugLog;
                if (Equals(DnsSettings.ListenIp, IPAddress.Any))
                    IsGlobal.IsChecked = true;

                DnsEnable.IsChecked = true;

                if (File.Exists($"{SetupBasePath}config.json"))
                    WindowState = WindowState.Minimized;
            }
            else
            {
                Snackbar.IsActive = true;
                if (Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName)
                        .Count(o => o.Id != Process.GetCurrentProcess().Id) > 0)
                {
                    var snackbarMsg = new SnackbarMessage()
                    {
                        Content = "可能已有一个正在运行的实例, 请不要重复启动！",
                        ActionContent = "退出",
                    };
                    snackbarMsg.ActionClick += (o, args) => Environment.Exit(Environment.ExitCode);
                    Snackbar.Message = snackbarMsg;
                    NotifyIcon.Text = @"AuroraDNS - [请不要重复启动]";
                }
                else
                {
                    Snackbar.Message = new SnackbarMessage() { Content = "DNS 服务器无法启动, 端口被占用。"};
                    NotifyIcon.Text = @"AuroraDNS - [端口被占用]";
                }

                DnsEnable.IsEnabled = false;
                ControlGrid.IsEnabled = false;
            }
        }

        private void IsGlobal_Checked(object sender, RoutedEventArgs e)
        {
            if (MyTools.PortIsUse(53))
            {
                MDnsSvrWorker.Dispose();
                MDnsServer = new DnsServer(IPAddress.Any, 10, 10);
                MDnsServer.QueryReceived += QueryResolve.ServerOnQueryReceived;
                Snackbar.MessageQueue.Enqueue(new TextBlock() {Text = "监听地址: 局域网 " + IPAddress.Any});
                MDnsSvrWorker.RunWorkerAsync();
            }
        }

        private void IsGlobal_Unchecked(object sender, RoutedEventArgs e)
        {
            if (MyTools.PortIsUse(53))
            {
                MDnsSvrWorker.Dispose();
                MDnsServer = new DnsServer(IPAddress.Loopback, 10, 10);
                MDnsServer.QueryReceived += QueryResolve.ServerOnQueryReceived;
                Snackbar.MessageQueue.Enqueue(new TextBlock() {Text = "监听地址: 本地 " + IPAddress.Loopback});
                MDnsSvrWorker.RunWorkerAsync();
            }
        }

        private void IsSysDns_Checked(object sender, RoutedEventArgs e)
        {
            if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                SysDnsSet.SetDns("127.0.0.1", DnsSettings.SecondDnsIp.ToString());
                Snackbar.MessageQueue.Enqueue(new TextBlock()
                {
                    Text = "主DNS:" + IPAddress.Loopback +
                           Environment.NewLine +
                           "辅DNS:" + DnsSettings.SecondDnsIp
                });
                IsSysDns.ToolTip = "已设为系统 DNS";
            }
            else
            {
                var snackbarMsg = new SnackbarMessage()
                {
                    Content = "权限不足",
                    ActionContent = "Administrator权限运行",
                };
                snackbarMsg.ActionClick += RunAsAdmin_OnActionClick;
                Snackbar.MessageQueue.Enqueue(snackbarMsg);
                IsSysDns.IsChecked = false;
            }
        }

        private void IsSysDns_Unchecked(object sender, RoutedEventArgs e)
        {
            if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
            {
                SysDnsSet.ResetDns();
                Snackbar.MessageQueue.Enqueue(new TextBlock() { Text = "已将 DNS 重置为自动获取" });
                IsSysDns.ToolTip = "设为系统 DNS";
            }
        }

        private void IsLog_Checked(object sender, RoutedEventArgs e)
        {
            DnsSettings.DebugLog = true;
            if (MyTools.PortIsUse(53))
                Snackbar.MessageQueue.Enqueue(new TextBlock() {Text = "记录日志:是"});
        }

        private void IsLog_Unchecked(object sender, RoutedEventArgs e)
        {
            DnsSettings.DebugLog = false;
            if (MyTools.PortIsUse(53))
                Snackbar.MessageQueue.Enqueue(new TextBlock() {Text = "记录日志:否"});
        }

        private void DnsEnable_Checked(object sender, RoutedEventArgs e)
        {
            MDnsSvrWorker.RunWorkerAsync();
            if (MDnsSvrWorker.IsBusy)
            {
                Snackbar.MessageQueue.Enqueue(new TextBlock() { Text = "DNS 服务器已启动" });
                NotifyIcon.Text = @"AuroraDNS - Running";
            }
        }

        private void DnsEnable_Unchecked(object sender, RoutedEventArgs e)
        {
            MDnsSvrWorker.Dispose();
            if (!MDnsSvrWorker.IsBusy)
            {
                Snackbar.MessageQueue.Enqueue(new TextBlock() { Text = "DNS 服务器已停止" });
                NotifyIcon.Text = @"AuroraDNS - Stop";
            }
        }

        private void SettingButton_Click(object sender, RoutedEventArgs e)
        {
            new SettingsWindow().ShowDialog();

            IsLog.IsChecked = DnsSettings.DebugLog;
            IsGlobal.IsChecked = Equals(DnsSettings.ListenIp, IPAddress.Any);

            if (DnsSettings.BlackListEnable && File.Exists("black.list"))
                DnsSettings.ReadBlackList();

            if (DnsSettings.WhiteListEnable && File.Exists("white.list"))
                DnsSettings.ReadWhiteList();

            if (DnsSettings.WhiteListEnable && File.Exists("rewrite.list"))
                DnsSettings.ReadWhiteList("rewrite.list");
        }

        private void RunAsAdmin_OnActionClick(object sender, RoutedEventArgs e)
        {
            try
            {
                MDnsSvrWorker.Dispose();
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = GetType().Assembly.Location,
                    Verb = "runas"
                };

                Process.Start(startInfo);
                Environment.Exit(Environment.ExitCode);
            }
            catch (Exception exception)
            {
                MyTools.BgwLog(exception.ToString());
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                FadeIn(0.25);
                ShowInTaskbar = true;
            }
            else if (WindowState == WindowState.Minimized)
                ShowInTaskbar = false;

            GC.Collect(2);
        }

        private void FadeIn(double sec)
        {
            var fadeInStoryboard = new Storyboard();
            DoubleAnimation fadeInAnimation = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromSeconds(sec)));
            Storyboard.SetTarget(fadeInAnimation, this);
            Storyboard.SetTargetProperty(fadeInAnimation, new PropertyPath(OpacityProperty));
            fadeInStoryboard.Children.Add(fadeInAnimation);

            Dispatcher.BeginInvoke(new Action(fadeInStoryboard.Begin), DispatcherPriority.Render, null);
        }

        private void MinimizedNormal(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                WindowState = WindowState.Minimized;
                Hide();
            }
            else if (WindowState == WindowState.Minimized)
            {
                Show();
                WindowState = WindowState.Normal;
            }
        }
    }
}
