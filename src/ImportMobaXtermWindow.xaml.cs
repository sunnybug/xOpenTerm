using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>选择 MobaXterm.ini 和可选密码文件，并按目录多选要导入的会话，确定后由主窗口按目录结构导入到当前父节点下。</summary>
public partial class ImportMobaXtermWindow : Window
{
    private List<MobaFolderNode> _folderRoots = new();

    /// <summary>用户点击确定时选中的会话（来自勾选的目录）；取消或未选则为空。</summary>
    public List<MobaXtermSessionItem> SelectedSessions { get; private set; } = new();

    /// <summary>若用户选择了密码文件，则为 配置名 → (Username, Password)；否则为 null。</summary>
    public IReadOnlyDictionary<string, (string Username, string Password)>? PasswordLookup { get; private set; }

    /// <summary>用户点击确定时使用的 MobaXterm.ini 路径，供主窗口保存为“上次使用”路径。</summary>
    public string? LastUsedIniPath { get; private set; }

    /// <summary>用户点击确定时使用的密码文件路径，供主窗口保存为“上次使用”路径。</summary>
    public string? LastUsedPasswordPath { get; private set; }

    public ImportMobaXtermWindow(Window owner, string? initialIniPath = null, string? initialPasswordPath = null)
    {
        InitializeComponent();
        Owner = owner;
        if (!string.IsNullOrWhiteSpace(initialIniPath))
            IniPathBox.Text = initialIniPath;
        else
            SuggestInitialPath();
        if (!string.IsNullOrWhiteSpace(initialPasswordPath))
            PasswordPathBox.Text = initialPasswordPath;
        Loaded += (_, _) => LoadIfPathSet();
    }

    private void SuggestInitialPath()
    {
        var doc = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var defaultPath = Path.Combine(doc, "MobaXterm", "MobaXterm.ini");
        if (File.Exists(defaultPath))
            IniPathBox.Text = defaultPath;
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 MobaXterm.ini",
            Filter = "配置文件 (*.ini)|*.ini|所有文件 (*.*)|*.*",
            FileName = string.IsNullOrEmpty(IniPathBox.Text) ? "MobaXterm.ini" : Path.GetFileName(IniPathBox.Text),
            InitialDirectory = string.IsNullOrEmpty(IniPathBox.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Path.GetDirectoryName(IniPathBox.Text)
        };
        if (dlg.ShowDialog() == true)
        {
            IniPathBox.Text = dlg.FileName;
            LoadSessions();
        }
    }

    private void BrowsePasswordBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择密码文件（可选）",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            FileName = string.IsNullOrEmpty(PasswordPathBox.Text) ? "" : Path.GetFileName(PasswordPathBox.Text),
            InitialDirectory = string.IsNullOrEmpty(PasswordPathBox.Text)
                ? (string.IsNullOrEmpty(IniPathBox.Text) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(IniPathBox.Text)!)
                : Path.GetDirectoryName(PasswordPathBox.Text)
        };
        if (dlg.ShowDialog() == true)
            PasswordPathBox.Text = dlg.FileName;
    }

    private void LoadSessions()
    {
        var path = IniPathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _folderRoots = new List<MobaFolderNode>();
            FolderTree.ItemsSource = null;
            OkBtn.IsEnabled = false;
            return;
        }
        var sessions = MobaXtermIniParser.Parse(path!);
        _folderRoots = MobaXtermIniParser.BuildFolderTree(sessions);
        FolderTree.ItemsSource = _folderRoots;
        OkBtn.IsEnabled = _folderRoots.Count > 0;
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = new List<MobaXtermSessionItem>();
        foreach (var root in _folderRoots)
            CollectSessionsFromSelectedFolders(root, selected, hasSelectedAncestor: false);
        SelectedSessions = selected;
        var pwdPath = PasswordPathBox.Text?.Trim();
        if (!string.IsNullOrEmpty(pwdPath) && File.Exists(pwdPath))
            PasswordLookup = MobaXtermIniParser.ParsePasswordFile(pwdPath);
        else
            PasswordLookup = null;
        LastUsedIniPath = IniPathBox.Text?.Trim();
        LastUsedPasswordPath = PasswordPathBox.Text?.Trim();
        DialogResult = true;
        Close();
    }

    /// <summary>从勾选的目录收集会话，仅从“最顶层”勾选目录收集，避免重复。</summary>
    private static void CollectSessionsFromSelectedFolders(MobaFolderNode node, List<MobaXtermSessionItem> into, bool hasSelectedAncestor)
    {
        if (node.IsSelected && !hasSelectedAncestor)
            node.CollectAllSessions(into);
        var ancestorSelected = hasSelectedAncestor || node.IsSelected;
        foreach (var sub in node.SubFolders)
            CollectSessionsFromSelectedFolders(sub, into, ancestorSelected);
    }

    /// <summary>若已设置路径，可调用此方法加载会话列表（如打开时自动加载）。</summary>
    public void LoadIfPathSet()
    {
        if (!string.IsNullOrWhiteSpace(IniPathBox.Text))
            LoadSessions();
    }
}
