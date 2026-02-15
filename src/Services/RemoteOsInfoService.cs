namespace xOpenTerm.Services;

/// <summary>远程操作系统信息（用于判断“查看进程流量”等命令）。与 UI、SSH 执行解耦，仅做解析与命令生成。</summary>
public sealed class RemoteOsInfo
{
    public OsFamily Family { get; init; }
    public bool NethogsInstalled { get; init; }
}

/// <summary>常见 Linux 发行版族，用于生成对应包管理器安装命令。</summary>
public enum OsFamily
{
    Unknown,
    Debian,
    RHEL,
    Fedora,
    Arch,
    OpenSUSE
}

/// <summary>远程 OS 信息收集与判断：提供检测命令、解析输出、根据 OS 与工具安装情况返回单条可复制命令。不依赖 WPF，不执行 SSH。</summary>
public static class RemoteOsInfoService
{
    private const string Delimiter = "---NETHOGS---";

    /// <summary>在远程执行以收集 os-release 与 nethogs 是否在 PATH 中的单条命令。</summary>
    public static readonly string DetectionCommand =
        "cat /etc/os-release 2>/dev/null || true; echo '" + Delimiter + "'; command -v nethogs 2>/dev/null || true";

    /// <summary>解析检测命令输出，得到 OS 族与 nethogs 是否已安装。无法识别或非 Linux 时返回 Unknown + NethogsInstalled=false。</summary>
    public static RemoteOsInfo? ParseDetectionOutput(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return new RemoteOsInfo { Family = OsFamily.Unknown, NethogsInstalled = false };

        var parts = output.Split(new[] { Delimiter }, 2, StringSplitOptions.None);
        var osRelease = parts.Length > 0 ? parts[0].Trim() : "";
        var nethogsLine = parts.Length > 1 ? parts[1].Trim() : "";

        var family = ParseOsFamily(osRelease);
        var nethogsInstalled = !string.IsNullOrWhiteSpace(nethogsLine);

        return new RemoteOsInfo { Family = family, NethogsInstalled = nethogsInstalled };
    }

    private static OsFamily ParseOsFamily(string osRelease)
    {
        if (string.IsNullOrWhiteSpace(osRelease))
            return OsFamily.Unknown;

        string? id = null;
        string? idLike = null;
        foreach (var line in osRelease.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var s = line.Trim();
            if (s.StartsWith("ID=", StringComparison.OrdinalIgnoreCase))
            {
                var v = s.Length > 3 ? s[3..].Trim('"', '\'').Trim() : "";
                if (!string.IsNullOrEmpty(v)) id = v;
            }
            else if (s.StartsWith("ID_LIKE=", StringComparison.OrdinalIgnoreCase))
            {
                var v = s.Length > 8 ? s[8..].Trim('"', '\'').Trim() : "";
                if (!string.IsNullOrEmpty(v)) idLike = v;
            }
        }

        var idLower = (id ?? "").ToLowerInvariant();
        var idLikeLower = (idLike ?? "").ToLowerInvariant();

        if (idLower is "debian" or "ubuntu" || idLikeLower.Contains("debian"))
            return OsFamily.Debian;
        if (idLower is "rhel" or "centos" or "rocky" or "alma" || idLikeLower.Contains("rhel") || idLikeLower.Contains("fedora"))
            return OsFamily.RHEL;
        if (idLower == "fedora")
            return OsFamily.Fedora;
        if (idLower is "arch" or "manjaro")
            return OsFamily.Arch;
        if (idLower is "opensuse-leap" or "opensuse-tumbleweed" || idLikeLower.Contains("suse"))
            return OsFamily.OpenSUSE;

        return OsFamily.Unknown;
    }

    /// <summary>根据远程 OS 信息返回应复制到剪贴板的一条命令与可选安装提示。info 为 null 或 Unknown 时返回通用命令 + 多系统安装提示。</summary>
    public static (string commandToCopy, string? installHint) GetProcessTrafficCommand(RemoteOsInfo? info)
    {
        if (info != null && info.NethogsInstalled)
            return ("sudo nethogs", null);

        if (info != null && info.Family != OsFamily.Unknown)
        {
            var installCmd = GetInstallCommandForFamily(info.Family);
            return (installCmd, null);
        }

        const string fallbackCommand = "sudo nethogs";
        const string fallbackHint = "Debian/Ubuntu: sudo apt install nethogs\nRHEL/CentOS:   sudo yum install nethogs\nFedora:        sudo dnf install nethogs\nArch:          sudo pacman -S nethogs\nopenSUSE:      sudo zypper install nethogs";
        return (fallbackCommand, fallbackHint);
    }

    private static string GetInstallCommandForFamily(OsFamily family)
    {
        return family switch
        {
            OsFamily.Debian => "sudo apt install nethogs",
            OsFamily.RHEL => "sudo yum install nethogs",
            OsFamily.Fedora => "sudo dnf install nethogs",
            OsFamily.Arch => "sudo pacman -S nethogs",
            OsFamily.OpenSUSE => "sudo zypper install nethogs",
            _ => "sudo apt install nethogs"
        };
    }
}
