using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace NolanWoWLauncher;

public sealed class RegisterWindow : Window
{
    private readonly string _registerUrl;
    private readonly TextBox _usernameBox;
    private readonly TextBox _emailBox;
    private readonly TextBox _passwordBox;
    private readonly TextBox _confirmPasswordBox;
    private readonly TextBox _messageBox;
    private readonly Button _submitButton;

    public RegisterWindow(string registerUrl)
    {
        _registerUrl = registerUrl;

        Title = "账号注册";
        Width = 500;
        Height = 640;
        MinWidth = 500;
        MinHeight = 640;
        MaxWidth = 500;
        MaxHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#0A1017"));
        SystemDecorations = SystemDecorations.Full;
        KeyDown += OnWindowKeyDown;

        _usernameBox = NewInput("请输入用户名");
        _emailBox = NewInput("请输入邮箱（可选）");
        _passwordBox = NewInput("请输入密码");
        _confirmPasswordBox = NewInput("请再次输入密码");

        _messageBox = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Height = 88,
            FontSize = 13,
            Background = new SolidColorBrush(Color.Parse("#101923")),
            Foreground = new SolidColorBrush(Color.Parse("#D9C8A5")),
            BorderBrush = new SolidColorBrush(Color.Parse("#705630")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        _submitButton = new Button
        {
            Content = "注册账号",
            Height = 48,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Background = new SolidColorBrush(Color.Parse("#D7AE63")),
            Foreground = Brushes.Black,
            BorderBrush = new SolidColorBrush(Color.Parse("#F0CB83")),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _submitButton.Click += async (_, _) => await SubmitAsync();

        var form = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(22),
            Children =
            {
                new TextBlock
                {
                    Text = "诺兰时光账号注册",
                    FontSize = 24,
                    FontWeight = FontWeight.Bold,
                    Foreground = new SolidColorBrush(Color.Parse("#F3E7CB"))
                },
                new TextBlock
                {
                    Text = "直接在启动器内完成注册，不再跳转外部网页。",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.Parse("#AFC2D4")),
                    Margin = new Thickness(0,0,0,8)
                },
                NewLabel("用户名（2-16位，仅字母数字下划线）"),
                _usernameBox,
                NewLabel("密码"),
                _passwordBox,
                NewLabel("确认密码"),
                _confirmPasswordBox,
                NewLabel("邮箱（可选）"),
                _emailBox,
                _submitButton,
                _messageBox
            }
        };

        Content = new Border
        {
            Margin = new Thickness(14),
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.Parse("#8A6729")),
            BorderThickness = new Thickness(1.2),
            Background = new SolidColorBrush(Color.Parse("#0D141D")),
            Child = form
        };
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            return;
        }

        if (e.Key == Key.Enter)
        {
            _ = SubmitAsync();
        }
    }

    private TextBlock NewLabel(string text) => new()
    {
        Text = text,
        FontSize = 14,
        Foreground = new SolidColorBrush(Color.Parse("#D9B97A"))
    };

    private TextBox NewInput(string watermark) => new()
    {
        Height = 42,
        Watermark = watermark,
        FontSize = 14,
        Padding = new Thickness(12, 10),
        Background = new SolidColorBrush(Color.Parse("#111A24")),
        Foreground = new SolidColorBrush(Color.Parse("#EAD7B0")),
        BorderBrush = new SolidColorBrush(Color.Parse("#705630")),
        BorderThickness = new Thickness(1.1),
        CornerRadius = new CornerRadius(8)
    };

    private async Task SubmitAsync()
    {
        var username = (_usernameBox.Text ?? string.Empty).Trim();
        var password = _passwordBox.Text ?? string.Empty;
        var confirmPassword = _confirmPasswordBox.Text ?? string.Empty;
        var email = (_emailBox.Text ?? string.Empty).Trim();

        if (username.Length < 2 || username.Length > 16)
        {
            SetMessage("用户名需要 2-16 位。", false);
            return;
        }

        foreach (var ch in username)
        {
            var ok = char.IsLetterOrDigit(ch) || ch == '_';
            if (!ok)
            {
                SetMessage("用户名只能包含字母、数字、下划线。", false);
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            SetMessage("密码不能为空。", false);
            return;
        }

        if (password.Length < 6)
        {
            SetMessage("密码至少需要 6 位。", false);
            return;
        }

        if (password != confirmPassword)
        {
            SetMessage("两次输入的密码不一致。", false);
            return;
        }

        try
        {
            _submitButton.IsEnabled = false;
            _submitButton.Content = "注册中...";
            SetMessage("正在提交注册...", true);

            var baseUri = new Uri(_registerUrl.EndsWith("/") ? _registerUrl : _registerUrl + "/");
            var apiUri = new Uri(baseUri, "register.php");

            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            var payload = JsonSerializer.Serialize(new
            {
                username,
                password,
                email
            });

            using var response = await http.PostAsync(
                apiUri,
                new StringContent(payload, Encoding.UTF8, "application/json"));

            var text = await response.Content.ReadAsStringAsync();

            if (string.IsNullOrWhiteSpace(text))
            {
                SetMessage("注册失败：服务端没有返回内容。", false);
                return;
            }

            bool success = false;
            string message;

            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;

                success = root.TryGetProperty("success", out var successNode) && successNode.GetBoolean();

                message = root.TryGetProperty("error", out var errorNode)
                    ? (errorNode.GetString() ?? "注册失败")
                    : root.TryGetProperty("message", out var msgNode)
                        ? (msgNode.GetString() ?? (success ? "注册成功！" : "注册失败"))
                        : (success ? "注册成功！" : "注册失败");
            }
            catch
            {
                message = response.IsSuccessStatusCode
                    ? $"服务器返回：{text}"
                    : $"注册失败：{text}";
            }

            SetMessage(message, success);

            if (success)
            {
                await Task.Delay(1200);
                Close();
            }
        }
        catch (TaskCanceledException)
        {
            SetMessage("注册失败：请求超时，请稍后重试。", false);
        }
        catch (Exception ex)
        {
            SetMessage($"注册失败：{ex.Message}", false);
        }
        finally
        {
            _submitButton.IsEnabled = true;
            _submitButton.Content = "注册账号";
        }
    }

    private void SetMessage(string message, bool ok)
    {
        _messageBox.Text = message;
        _messageBox.Foreground = new SolidColorBrush(Color.Parse(ok ? "#7BE07B" : "#FF8B8B"));
    }
}
