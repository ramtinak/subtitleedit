﻿using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.Http;
using Nikse.SubtitleEdit.Logic;
using Nikse.SubtitleEdit.Logic.Plugins;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using MessageBox = Nikse.SubtitleEdit.Forms.SeMsgBox.MessageBox;

namespace Nikse.SubtitleEdit.Forms
{
    public sealed partial class PluginsGet : Form
    {
        private List<PluginInfoItem> _downloadList;
        private string _downloadedPluginName;
        private readonly LanguageStructure.PluginsGet _language;
        private List<string> _updateAllListUrls;
        private bool _updatingAllPlugins;
        private int _updatingAllPluginsCount;
        private bool _fetchingData;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public PluginsGet()
        {
            UiUtil.PreInitialize(this);
            InitializeComponent();
            UiUtil.FixFonts(this);
            _language = LanguageSettings.Current.PluginsGet;
            Text = _language.Title;
            tabPageInstalledPlugins.Text = _language.InstalledPlugins;
            tabPageGetPlugins.Text = _language.GetPlugins;

            buttonDownload.Text = _language.Download;
            buttonRemove.Text = _language.Remove;
            buttonOK.Text = LanguageSettings.Current.General.Ok;
            linkLabelOpenPluginFolder.Text = _language.OpenPluginsFolder;
            labelDescription1.Text = _language.GetPluginsInfo1;
            labelClickDownload.Text = _language.GetPluginsInfo2;

            columnHeaderName.Text = LanguageSettings.Current.General.Name;
            columnHeaderDescription.Text = _language.Description;
            columnHeaderVersion.Text = _language.Version;
            columnHeaderDate.Text = _language.Date;

            columnHeaderInsName.Text = LanguageSettings.Current.General.Name;
            columnHeaderInsDescription.Text = _language.Description;
            columnHeaderInsVersion.Text = _language.Version;
            columnHeaderInsType.Text = _language.Type;

            labelShortcutsSearch.Text = LanguageSettings.Current.General.Search;
            labelShortcutsSearch.Left = textBoxSearch.Left - labelShortcutsSearch.Width - 9;
            buttonSearchClear.Text = LanguageSettings.Current.DvdSubRip.Clear;

            buttonUpdateAll.Visible = false;
            _cancellationTokenSource = new CancellationTokenSource();
        }

        private static string GetPluginXmlFileUrl()
        {
            return "https://raw.github.com/SubtitleEdit/plugins/master/Plugins4.xml";
        }

        private static string GetPluginFolder()
        {
            var pluginsFolder = Configuration.PluginsDirectory;
            if (!Directory.Exists(pluginsFolder))
            {
                try
                {
                    Directory.CreateDirectory(pluginsFolder);
                }
                catch (Exception exception)
                {
                    MessageBox.Show($"Unable to create plugin folder {pluginsFolder}: {exception.Message}");
                    return null;
                }
            }

            return pluginsFolder;
        }

        private void GetAndShowAllPluginInfo()
        {
            try
            {
                _fetchingData = true;
                labelPleaseWait.Text = LanguageSettings.Current.General.PleaseWait;
                Refresh();
                ShowInstalledPlugins();
                ShowOnlinePlugins();
                labelPleaseWait.Text = string.Empty;
            }
            catch (Exception exception)
            {
                labelPleaseWait.Text = string.Empty;
                ChangeControlsState(true);
                MessageBox.Show($"Unable to get plugin list!" + Environment.NewLine + Environment.NewLine +
                                    exception.Message + Environment.NewLine + Environment.NewLine + exception.StackTrace);
            }
            _fetchingData = false;
        }

        private void ShowOnlinePlugins()
        {
            _downloadList = new List<PluginInfoItem>();
            listViewGetPlugins.BeginUpdate();
            _updateAllListUrls = new List<string>();
            var onlinePluginInfo = new OnlinePluginMetadataProvider(GetPluginXmlFileUrl());
            LoadAvailablePlugins(onlinePluginInfo.GetPlugins());
            ShowAvailablePlugins();
            listViewGetPlugins.EndUpdate();

            if (_updateAllListUrls.Count > 0)
            {
                buttonUpdateAll.BackColor = Configuration.Settings.General.UseDarkTheme ? Color.Green : Color.LightGreen;
                if (LanguageSettings.Current.PluginsGet.UpdateAllX != null)
                {
                    buttonUpdateAll.Text = string.Format(LanguageSettings.Current.PluginsGet.UpdateAllX, _updateAllListUrls.Count);
                }
                else
                {
                    buttonUpdateAll.Text = LanguageSettings.Current.PluginsGet.UpdateAll;
                }

                buttonUpdateAll.Visible = true;
            }
        }

        private void LoadAvailablePlugins(IReadOnlyCollection<PluginInfoItem> plugins)
        {
            foreach (var item in plugins)
            {
                _downloadList.Add(item);

                foreach (ListViewItem installed in listViewInstalledPlugins.Items)
                {
                    if (string.Compare(installed.Text, item.Name.Trim('.'), StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        var installedVer = MakeComparableVersionNumber(installed.SubItems[2].Text);
                        var currentVer = MakeComparableVersionNumber(item.Version.ToString(CultureInfo.InvariantCulture));
                        if (installedVer < currentVer)
                        {
                            installed.BackColor = Configuration.Settings.General.UseDarkTheme ? Color.IndianRed : Color.LightPink;
                            installed.SubItems[1].Text = $"{_language.UpdateAvailable} {installed.SubItems[1].Text}";
                            buttonUpdateAll.Visible = true;
                            _updateAllListUrls.Add(item.Url);
                        }
                    }
                }
            }
        }

        private static long MakeComparableVersionNumber(string versionNumber)
        {
            var s = versionNumber.Replace(',', '.').Replace(" ", string.Empty);
            var arr = s.Split('.');
            if (arr.Length == 1 && long.TryParse(arr[0], out var a0))
            {
                return a0 * 1_000_000;
            }

            if (arr.Length == 2 && long.TryParse(arr[0], out var b0) && long.TryParse(arr[1], out var b1))
            {
                return b0 * 1_000_000 + b1 * 1_000;
            }

            if (arr.Length == 3 && long.TryParse(arr[0], out var c0) && long.TryParse(arr[1], out var c1) && long.TryParse(arr[2], out var c2))
            {
                return c0 * 1_000_000 + c1 * 1_000 + c2;
            }

            SeLogger.Error("Bad plugin version number: " + versionNumber);
            return 0;
        }

        private void ShowAvailablePlugins()
        {
            if (_downloadList == null)
            {
                return;
            }

            var search = textBoxSearch.Text.Length > 1;
            var searchText = textBoxSearch.Text;
            listViewGetPlugins.BeginUpdate();
            if (listViewGetPlugins.Items.Count > 0)
            {
                listViewGetPlugins.Items.Clear();
            }

            foreach (var plugin in _downloadList)
            {
                var item = new ListViewItem(plugin.Name) { Tag = plugin };
                item.SubItems.Add(plugin.Description);
                item.SubItems.Add(plugin.Version.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(plugin.Date);

                if (!search ||
                    plugin.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    plugin.Description.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                    plugin.Version.ToString(CultureInfo.InvariantCulture).Contains(searchText, StringComparison.OrdinalIgnoreCase))
                {
                    listViewGetPlugins.Items.Add(item);
                }
            }
            listViewGetPlugins.EndUpdate();
        }

        private void ShowInstalledPlugins()
        {
            listViewInstalledPlugins.BeginUpdate();
            listViewInstalledPlugins.Items.Clear();
            var localPluginInfo = new InstalledPluginMetadataProvider();
            foreach (var pluginInfo in localPluginInfo.GetPlugins())
            {
                var item = new ListViewItem(pluginInfo.Name) { Tag = pluginInfo };
                item.SubItems.Add(pluginInfo.Description);
                item.SubItems.Add(pluginInfo.Version.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(pluginInfo.ActionType);
                listViewInstalledPlugins.Items.Add(item);
            }
            listViewInstalledPlugins.EndUpdate();
        }

        private void buttonDownload_Click(object sender, EventArgs e)
        {
            if (listViewGetPlugins.SelectedItems.Count == 0)
            {
                return;
            }

            _updatingAllPlugins = false;
            var plugin = (PluginInfoItem)listViewGetPlugins.SelectedItems[0].Tag;
            _downloadedPluginName = plugin.Name;
            var url = plugin.Url;
            try
            {
                labelPleaseWait.Text = LanguageSettings.Current.General.PleaseWait;
                ChangeControlsState(false);
                Refresh();
                Cursor = Cursors.WaitCursor;

                using (var httpClient = DownloaderFactory.MakeHttpClient())
                using (var downloadStream = new MemoryStream())
                {
                    var downloadTask = httpClient.DownloadAsync(url, downloadStream, new Progress<float>((progress) =>
                    {
                        var pct = (int)Math.Round(progress * 100.0, MidpointRounding.AwayFromZero);
                        labelPleaseWait.Text = LanguageSettings.Current.General.PleaseWait + "  " + pct + "%";
                    }), _cancellationTokenSource.Token);

                    while (!downloadTask.IsCompleted && !downloadTask.IsCanceled)
                    {
                        Application.DoEvents();
                    }

                    if (downloadTask.IsCanceled)
                    {
                        DialogResult = DialogResult.Cancel;
                        labelPleaseWait.Refresh();
                        return;
                    }

                    DownloadDataCompleted(downloadStream);
                }
            }
            catch (Exception exception)
            {
                ChangeControlsState(true);
                Cursor = Cursors.Default;
                MessageBox.Show($"Unable to download {url}!" + Environment.NewLine + Environment.NewLine +
                                exception.Message + Environment.NewLine + Environment.NewLine + exception.StackTrace);
            }
        }

        private void DownloadDataCompleted(MemoryStream downloadStream)
        {
            labelPleaseWait.Text = string.Empty;
            if (downloadStream.Length == 0)
            {
                throw new Exception("No content downloaded - missing file or no internet connection!");
            }

            var pluginsFolder = GetPluginFolder();

            downloadStream.Position = 0;
            using (var zip = ZipExtractor.Open(downloadStream))
            {
                var dir = zip.ReadCentralDir();

                // Extract dic/aff files in dictionary folder
                foreach (ZipExtractor.ZipFileEntry entry in dir)
                {
                    var fileName = Path.GetFileName(entry.FilenameInZip);
                    var fullPath = Path.Combine(pluginsFolder, fileName);
                    if (File.Exists(fullPath))
                    {
                        try
                        {
                            File.Delete(fullPath);
                        }
                        catch
                        {
                            MessageBox.Show($"{fullPath} already exists - unable to overwrite it");
                            Cursor = Cursors.Default;
                            ChangeControlsState(true);
                            return;
                        }
                    }
                    zip.ExtractFile(entry, fullPath);
                }
            }

            Cursor = Cursors.Default;
            ChangeControlsState(true);
            if (_updatingAllPlugins)
            {
                _updatingAllPluginsCount++;
                if (_updatingAllPluginsCount == _updateAllListUrls.Count)
                {
                    MessageBox.Show(string.Format(_language.XPluginsUpdated, _updatingAllPluginsCount));
                }
            }
            else
            {
                MessageBox.Show(string.Format(_language.PluginXDownloaded, _downloadedPluginName));
            }

            ShowInstalledPlugins();
        }

        private void ChangeControlsState(bool enable)
        {
            if (enable)
            {
                labelPleaseWait.Text = string.Empty;
            }
            buttonOK.Enabled = enable;
            buttonDownload.Enabled = enable;
            listViewGetPlugins.Enabled = enable;
        }

        private void linkLabelOpenDictionaryFolder_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            UiUtil.OpenFolder(GetPluginFolder());
        }

        private void PluginsGet_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
            }
            else if (e.KeyData == UiUtil.HelpKeys)
            {
                UiUtil.ShowHelp("#plugins");
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.F5 && !_fetchingData)
            {
                GetAndShowAllPluginInfo();
            }
        }

        private void PluginsGet_ResizeEnd(object sender, EventArgs e)
        {
            listViewGetPlugins.AutoSizeLastColumn();
            listViewInstalledPlugins.AutoSizeLastColumn();
        }

        private void PluginsGet_Shown(object sender, EventArgs e)
        {
            GetAndShowAllPluginInfo();
            PluginsGet_ResizeEnd(sender, e);
        }

        private void buttonRemove_Click(object sender, EventArgs e)
        {
            if (listViewInstalledPlugins.SelectedItems.Count < 1)
            {
                return;
            }

            var fileName = listViewInstalledPlugins.SelectedItems[0].Tag.ToString();
            var index = listViewInstalledPlugins.SelectedItems[0].Index;
            if (File.Exists(fileName))
            {
                try
                {
                    File.Delete(fileName);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(exception.Message);
                    return;
                }
            }
            listViewInstalledPlugins.Items.RemoveAt(index);
            if (index >= listViewInstalledPlugins.Items.Count)
            {
                index--;
            }

            if (index >= 0)
            {
                listViewInstalledPlugins.Items[index].Selected = true;
                listViewInstalledPlugins.Items[index].Focused = true;
            }
            buttonUpdateAll.Visible = false;
            GetAndShowAllPluginInfo();
        }

        private void buttonUpdateAll_Click(object sender, EventArgs e)
        {
            buttonUpdateAll.Enabled = false;
            buttonUpdateAll.BackColor = DefaultBackColor;
            try
            {
                labelPleaseWait.Text = LanguageSettings.Current.General.PleaseWait;
                ChangeControlsState(false);
                Refresh();
                Cursor = Cursors.WaitCursor;

                using (var httpClient = DownloaderFactory.MakeHttpClient())
                {
                    _updatingAllPluginsCount = 0;
                    _updatingAllPlugins = true;
                    for (var i = 0; i < _updateAllListUrls.Count; i++)
                    {
                        using (var downloadStream = new MemoryStream())
                        {
                            var downloadTask = httpClient.DownloadAsync(_updateAllListUrls[i], downloadStream, new Progress<float>((progress) =>
                            {
                                var pct = (int)Math.Round(progress * 100.0, MidpointRounding.AwayFromZero);
                                labelPleaseWait.Text = LanguageSettings.Current.General.PleaseWait + "  " + pct + "%";
                            }), _cancellationTokenSource.Token);

                            while (!downloadTask.IsCompleted && !downloadTask.IsCanceled)
                            {
                                Application.DoEvents();
                            }

                            if (downloadTask.IsCanceled)
                            {
                                DialogResult = DialogResult.Cancel;
                                labelPleaseWait.Refresh();
                                return;
                            }

                            DownloadDataCompleted(downloadStream);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                ChangeControlsState(true);
                Cursor = Cursors.Default;
                MessageBox.Show(exception.Message + Environment.NewLine + Environment.NewLine + exception.StackTrace);
            }
        }

        private void buttonSearchClear_Click(object sender, EventArgs e)
        {
            textBoxSearch.Text = string.Empty;
        }

        private void textBoxSearch_TextChanged(object sender, EventArgs e)
        {
            listViewGetPlugins.BeginUpdate();
            listViewGetPlugins.Items.Clear();
            ShowAvailablePlugins();
            listViewGetPlugins.EndUpdate();
            buttonSearchClear.Enabled = textBoxSearch.Text.Length > 0;
        }

        private void listViewInstalledPlugins_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            var lv = (ListView)sender;

            if (!(lv.ListViewItemSorter is ListViewSorter sorter))
            {
                sorter = new ListViewSorter
                {
                    ColumnNumber = e.Column,
                    IsNumber = false,
                    Descending = true,
                };
                lv.ListViewItemSorter = sorter;
            }

            if (e.Column == sorter.ColumnNumber)
            {
                sorter.Descending = !sorter.Descending; // inverse sort direction
            }
            else
            {
                sorter.ColumnNumber = e.Column;
                sorter.Descending = false;
                sorter.IsNumber = false;
            }

            lv.Sort();
        }
    }
}
