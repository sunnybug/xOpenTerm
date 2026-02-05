using Renci.SshNet;
using xOpenTerm.Models;

namespace xOpenTerm.Services;

/// <summary>SSH 连接测试（用于节点/凭证/隧道中的“测试连接”按钮）</summary>
public static class SshTester
{
    public static bool Test(string host, ushort port, string username,
        string? password, string? keyPath, string? keyPassphrase, bool useAgent = false)
    {
        try
        {
            ConnectionInfo? conn;
            if (useAgent)
            {
                conn = SessionManager.CreateConnectionInfo(host, port, username, null, null, null, true);
            }
            else if (!string.IsNullOrEmpty(keyPath))
            {
                var keyFile = new PrivateKeyFile(keyPath, keyPassphrase);
                conn = new ConnectionInfo(host, port, username, new PrivateKeyAuthenticationMethod(username, keyFile));
            }
            else if (!string.IsNullOrEmpty(password))
            {
                conn = new ConnectionInfo(host, port, username, new PasswordAuthenticationMethod(username, password));
            }
            else
            {
                return false;
            }
            if (conn == null) return false;
            using var client = new SshClient(conn);
            client.Connect();
            client.Disconnect();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
