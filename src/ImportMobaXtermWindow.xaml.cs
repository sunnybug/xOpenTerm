using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using xOpenTerm.Services;

namespace xOpenTerm;

/// <summary>选择 MobaXterm.ini 并多选要导入的会话，确定后由主窗口将选中项导入到当前父节点下。</summary>
public partial class ImportMobaXtermWindow : Window
{
    private List<MobaXtermSessionItem> _allItems = new();

    /// <summary>用户点击确定时选中的会话；取消或未选则为空。</summary>
    public List<MobaXtermSessionItem> SelectedSessions { get; private set; } = new();

    public ImportMobaXtermWindow(Window owner)
    {
        InitializeComponent();
        Owner = owner;
        SuggestInitialPath();
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

    private void LoadSessions()
    {
        var path = IniPathBox.Text?.Trim();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _allItems = new List<MobaXtermSessionItem>();
            SessionList.ItemsSource = null;
            OkBtn.IsEnabled = false;
            return;
        }
        _allItems = MobaXtermIniParser.Parse(path!);
        SessionList.ItemsSource = _allItems;
        OkBtn.IsEnabled = _allItems.Count > 0;
    }

    private void OkBtn_Click(object sender, RoutedEventArgs e)
    {
        var selected = new List<MobaXtermSessionItem>();
        foreach (var item in SessionList.SelectedItems)
            if (item is MobaXtermSessionItem si)
                selected.Add(si);
        SelectedSessions = selected;
        DialogResult = true;
        Close();
    }

    /// <summary>若已设置路径，可调用此方法加载会话列表（如打开时自动加载）。</summary>
    public void LoadIfPathSet()
    {
        if (!string.IsNullOrWhiteSpace(IniPathBox.Text))
            LoadSessions();
    }
}
