﻿using Microsoft.Win32;
using NetUpgradePlanner.Mvvm;
using NetUpgradePlanner.Services;

using System;
using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Input;
using SharpCompress.Archives.Zip;
using Terrajobst.NetUpgradePlanner;
using ZipArchive = SharpCompress.Archives.Zip.ZipArchive;
using ZipArchiveEntry = SharpCompress.Archives.Zip.ZipArchiveEntry;

namespace NetUpgradePlanner.ViewModels.MainWindow;

internal sealed class MainWindowViewModel : ViewModel
{
    private readonly ProgressService _progressService;
    private readonly WorkspaceService _workspaceService;
    private readonly WorkspaceDocumentService _workspaceDocumentService;
    private readonly CatalogService _catalogService;
    private readonly TelemetryService _telemetryService;
    private readonly UpdateService _updateService;
    private readonly OfflineDetectionService _offlineDetectionService;
    private bool _isEmpty = true;
    private string _title;
    private bool _hasApplicationUpdate;

    public MainWindowViewModel(ProgressViewModel progress,
                               ProgressService progressService,
                               WorkspaceService workspaceService,
                               WorkspaceDocumentService workspaceDocumentService,
                               CatalogService catalogService,
                               TelemetryService telemetryService,
                               UpdateService updateService,
                               OfflineDetectionService offlineDetectionService)
    {
        _workspaceService = workspaceService;
        _workspaceService.Changed += WorkspaceService_Changed;
        _workspaceDocumentService = workspaceDocumentService;
        _workspaceDocumentService.Changed += WorkspaceDocumentService_Changed;

        _catalogService = catalogService;
        _telemetryService = telemetryService;
        _updateService = updateService;
        _updateService.Changed += UpdateService_Changed;
        Progress = progress;
        _progressService = progressService;
        _offlineDetectionService = offlineDetectionService;
        NewCommand = new Command(async () => await NewAsync(), () => !_progressService.IsRunning);
        OpenCommand = new Command(async () => await OpenAsync(), () => !_progressService.IsRunning);
        SaveCommand = new Command(async () => await SaveAsync(), () => !_progressService.IsRunning);
        SaveAsCommand = new Command(async () => await SaveAsAsync(), () => !_progressService.IsRunning);
        SaveReportCommand = new Command(async () => await SaveReportAsync(), () => !_progressService.IsRunning);
        AddFilesCommand = new Command(async () => await AddFilesAsync(), () => !_progressService.IsRunning);
        AddFolderCommand = new Command(async () => await AddFolderAsync(), () => !_progressService.IsRunning);
        AnalyzeCommand = new Command(async () => await AnalyzeAsync(), () => !_workspaceService.Current.AssemblySet.IsEmpty && !_progressService.IsRunning);
        CreateOfflineCopyCommand = new Command(async () => await CreateOfflineCopyAsync(), () => !_progressService.IsRunning && !_offlineDetectionService.IsOfflineInstallation);
        UpdateCatalogCommand = new Command(async () => await UpdateCatalogAsync(), () => !_progressService.IsRunning && !_offlineDetectionService.IsOfflineInstallation);
        SendFeedbackCommand = new Command(() => SendFeedback(), () => !_offlineDetectionService.IsOfflineInstallation);
        CheckForApplicationUpdateCommand = new Command(async () => await CheckForApplicationUpdateAsync(), () => !_progressService.IsRunning &&  !_offlineDetectionService.IsOfflineInstallation);
        UpdateApplicationCommand = new Command(async () => await UpdateApplicationAsync(), () => !_progressService.IsRunning && !_offlineDetectionService.IsOfflineInstallation);
        AboutCommand = new Command(() => About());
        
        UpdateTitle();
    }

    public ProgressViewModel Progress { get; }

    public ICommand NewCommand { get; }

    public ICommand OpenCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand SaveAsCommand { get; }

    public ICommand SaveReportCommand { get; }

    public ICommand AddFilesCommand { get; }

    public ICommand AddFolderCommand { get; }

    public ICommand AnalyzeCommand { get; }

    public ICommand CreateOfflineCopyCommand { get; }

    public ICommand UpdateCatalogCommand { get; }

    public ICommand SendFeedbackCommand { get; }

    public ICommand CheckForApplicationUpdateCommand { get; }
    
    public ICommand UpdateApplicationCommand { get; }

    public ICommand AboutCommand { get; }

    public bool IsOnline
    {
        get => !_offlineDetectionService.IsOfflineInstallation;
    }
    
    public bool HasApplicationUpdate
    {
        get => _hasApplicationUpdate;
        set
        {
            if (_hasApplicationUpdate != value)
            {
                _hasApplicationUpdate = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        set
        {
            if (_isEmpty != value)
            {
                _isEmpty = value;
                OnPropertyChanged();
            }
        }
    }

    public bool SendTelemetry
    {
        get => _telemetryService.IsEnabled;
        set
        {
            if (_telemetryService.IsEnabled != value)
            {
                _telemetryService.IsEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public string Title
    {
        get => _title;
        [MemberNotNull(nameof(_title))]
        set
        {
            if (_title != value)
            {
                _title = value;
                OnPropertyChanged();
            }
        }
    }

    [MemberNotNull(nameof(_title))]
    private void UpdateTitle()
    {
        var baseTitle =
            _offlineDetectionService.IsOfflineInstallation
                ? $"{ThisAssembly.AssemblyTitle} [Offline]"
                : ThisAssembly.AssemblyTitle;

        var isDefault = _workspaceService.Current == Workspace.Default;
        if (isDefault)
        {
            Title = baseTitle;
        }
        else
        {
            var isDirty = _workspaceDocumentService.IsDirty;
            var fileName = _workspaceDocumentService.FileName ?? "Untitled";
            var dirtyMarker = isDirty ? "*" : string.Empty;
            Title = $"{baseTitle} - {fileName}{dirtyMarker}";
        }
    }

    public bool? ConfirmSave()
    {
        if (!_workspaceDocumentService.IsDirty)
            return false;

        var text = "Save unchanged changes?";
        var caption = "Save Changes";
        var buttons = MessageBoxButton.YesNoCancel;
        var image = MessageBoxImage.Question;
        var defaultButton = MessageBoxResult.Yes;
        var result = MessageBox.Show(Application.Current.MainWindow, text, caption, buttons, image, defaultButton);

        if (result == MessageBoxResult.Cancel)
            return null;

        return false;
    }

    public async Task<bool> ConfirmSavingUnchangedChangesAsync()
    {
        var result = ConfirmSave();
        return result is false ||
               result is true && await SaveAsync();
    }

    private async Task NewAsync()
    {
        if (!await ConfirmSavingUnchangedChangesAsync())
            return;

        _workspaceDocumentService.Clear();
    }

    private async Task OpenAsync()
    {
        if (!await ConfirmSavingUnchangedChangesAsync())
            return;

        var dialog = new OpenFileDialog();
        dialog.Filter = $"{ThisAssembly.AssemblyTitle} projects (*.nupproj)|*.nupproj";

        if (dialog.ShowDialog() == true)
        {
            await _workspaceDocumentService.LoadAsync(dialog.FileName);
        }
    }

    public async Task<bool> SaveAsync()
    {
        if (_workspaceDocumentService.FileName is null)
            return await SaveAsAsync();

        await _workspaceDocumentService.SaveAsync(_workspaceDocumentService.FileName);
        return true;
    }

    private async Task<bool> SaveAsAsync()
    {
        var dialog = new SaveFileDialog();
        dialog.Filter = $"{ThisAssembly.AssemblyTitle} projects (*.nupproj)|*.nupproj";

        if (dialog.ShowDialog() != true)
            return false;

        await _workspaceDocumentService.SaveAsync(dialog.FileName);
        return true;
    }

    private async Task SaveReportAsync()
    {
        var dialog = new SaveFileDialog();
        dialog.Title = "Save Report";
        dialog.Filter = "Excel Workbooks (*.xlsx)|*.xlsx";
        dialog.FileName = _workspaceDocumentService.FileName is not null
            ? Path.GetFileNameWithoutExtension(_workspaceDocumentService.FileName)
            : "Untitled";

        if (dialog.ShowDialog() != true)
            return;

        await _progressService.Run(async _ => await WorkspacePersistenceExcel.SaveAsync(_workspaceService.Current, dialog.FileName),
                                   "Saving as Excel Workbook");

        var fileTypeIsRegistered = Registry.ClassesRoot.OpenSubKey(".xlsx", false) is not null;
        if (fileTypeIsRegistered)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dialog.FileName,
                UseShellExecute = true
            });
        }
    }

    private async Task AddFilesAsync()
    {
        var dialog = new OpenFileDialog();
        dialog.Multiselect = true;
        dialog.Filter = "Assemblies (*.dll;*.exe)|*.dll;*.exe|All Files (*.*)|*.*";

        if (dialog.ShowDialog() == true)
            await _workspaceService.AddAssembliesAsync(dialog.FileNames);
    }

    private async Task AddFolderAsync()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog();

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var paths = Directory.GetFiles(dialog.SelectedPath, "*", SearchOption.AllDirectories)
                                 .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".dll" or ".exe");

            await _workspaceService.AddAssembliesAsync(paths);
        }
    }

    private async Task AnalyzeAsync()
    {
        await _workspaceService.AnalyzeAsync();
    }

    private async Task CreateOfflineCopyAsync()
    {
        var processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (processDirectory is null)
            return;
        
        var dialog = new SaveFileDialog();
        dialog.Filter = $"ZIP Archives (*.zip)|*.zip";
        dialog.FileName = "NetUpgradePlanner_Offline.zip";

        if (dialog.ShowDialog() != true)
            return;

        var zipFileName = dialog.FileName;

        await UpdateCatalogAsync();
        await _progressService.Run(_ =>
        {
            if (File.Exists(zipFileName))
                File.Delete(zipFileName);
            ZipFile.CreateFromDirectory(processDirectory, zipFileName, CompressionLevel.Optimal, includeBaseDirectory: false);
            using var archive = ZipFile.Open(zipFileName, ZipArchiveMode.Update);
            archive.CreateEntry("Offline.txt");
        }, "Creating ZIP file...");
    }
    
    private async Task UpdateCatalogAsync()
    {
        await _catalogService.UpdateAsync();
    }

    private void SendFeedback()
    {
        var version = ThisAssembly.AssemblyInformationalVersion;
        var semVer = version.Contains('+')
                        ? version.Substring(0, version.IndexOf('+'))
                        : version;

        var title = "";
        var body = new StringBuilder()
            .AppendLine("<!-- Describe the feature request or bug -->")
            .AppendLine("")
            .AppendLine("### Version")
            .AppendLine()
            .AppendLine($"{semVer} ({ThisAssembly.GitCommitId})")
            .ToString();

        var uri = new Uri($"https://github.com/dotnet/apisof.net/issues/new?title={HttpUtility.UrlEncode(title)}&body={HttpUtility.UrlEncode(body)}&labels=area-upgrade-planner");
        BrowserService.NavigateTo(uri);
    }

    private async Task CheckForApplicationUpdateAsync()
    {
        var hasUpdate = await _updateService.CheckForUpdateAsync();

        if (!hasUpdate)
        {
            MessageBox.Show($"{ThisAssembly.AssemblyTitle} is up-to-date.",
                            ThisAssembly.AssemblyTitle,
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }
        else
        {
            var result = MessageBox.Show("Update available. Update now?", 
                                         ThisAssembly.AssemblyTitle,
                                         MessageBoxButton.YesNo,
                                         MessageBoxImage.Question,
                                         MessageBoxResult.Yes);
            if (result == MessageBoxResult.Yes)
                await UpdateApplicationAsync();
        }
    }

    private async Task UpdateApplicationAsync()
    {
        if (await ConfirmSavingUnchangedChangesAsync())
            await _updateService.UpdateAsync();
    }

    private static void About()
    {
        var sb = new StringBuilder();
        sb.AppendLine(ThisAssembly.AssemblyInformationalVersion);
        sb.AppendLine();
        sb.AppendLine(ThisAssembly.GitCommitId);
        sb.AppendLine(ThisAssembly.GitCommitDate.ToString());

        var text = sb.ToString();

        MessageBox.Show(text,
                        ThisAssembly.AssemblyTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
    }

    private void WorkspaceService_Changed(object? sender, EventArgs e)
    {
        IsEmpty = _workspaceService.Current.AssemblySet.Entries.Count == 0;
    }

    private void WorkspaceDocumentService_Changed(object? sender, EventArgs e)
    {
        UpdateTitle();
    }

    private void UpdateService_Changed(object? sender, EventArgs e)
    {
        HasApplicationUpdate = _updateService.HasUpdate;
    }
}
