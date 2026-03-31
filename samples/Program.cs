using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Notifications;
using Microsoft.Toolkit.Uwp.Notifications;
using System;

// 初始化 Toast 通知服务（必须在发送通知前调用）
ToastNotificationService.Initialize();

// 注册按钮事件处理器
ToastNotificationService.RegisterActionHandler("accept", args =>
{
    MessageBox.Show($"✅ 确认参加会议 - 参数: {args.Arguments}");
});

ToastNotificationService.RegisterActionHandler("snooze", args =>
{
    MessageBox.Show($"⏰ 稍后提醒 - 参数: {args.Arguments}");
});

ToastNotificationService.RegisterActionHandler("cancel", args =>
{
    MessageBox.Show($"❌ 取消 - 参数: {args.Arguments}");
});

// 注册未处理事件的兜底处理器
ToastNotificationService.OnUnhandledAction += (s, args) =>
{
    MessageBox.Show($"⚠️ 未处理的 Action: {args.Action}");
};

var app = new Application();

// 创建主窗口
var window = new Window
{
    Title = "Toast 通知测试",
    Width = 600,
    Height = 500,
    Content = CreateMainContent()
};

// 检查是否由 Toast 激活启动
if (ToastNotificationService.WasActivatedByToast)
{
    MessageBox.Show("🚀 应用通过点击 Toast 通知启动");
}

app.Run(window);

// 创建主界面内容
UIElement CreateMainContent()
{
    var stackPanel = new StackPanel
    {
        Margin = new Thickness(30),
    };

    var title = new TextBlock
    {
        Text = "Toast 通知测试",
        FontSize = 24,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 0, 0, 20)
    };

    // 1. 简单通知按钮
    var simpleBtn = new Button
    {
        Content = "发送简单通知",
        Width = 200,
        Height = 40
    };
    simpleBtn.Click += (s, e) =>
    {
        ToastNotificationService.Show("新消息", "这是一条简单的测试通知", "simpleAction");
    };

    // 2. 带按钮的通知
    var buttonBtn = new Button
    {
        Content = "发送带按钮通知",
        Width = 200,
        Height = 40
    };
    buttonBtn.Click += (s, e) =>
    {
        ToastNotificationService.ShowWithButtons(
            "会议提醒",
            "15分钟后有团队会议",
            new ToastButtonInfo("确认参加", "accept", "btnAccept"),
            new ToastButtonInfo("稍后提醒", "snooze", "btnSnooze"),
            new ToastButtonInfo("取消", "cancel", "btnCancel")
        );
    };

    // 3. 带输入框的通知
    var inputBtn = new Button
    {
        Content = "发送快速回复通知",
        Width = 200,
        Height = 40
    };
    inputBtn.Click += (s, e) =>
    {
        ToastNotificationService.ShowWithInput(
            "新评论",
            "有人评论了你的帖子",
            "replyText",
            "输入回复...",
            "发送",
            "submitReply",
            "btnSubmit"
        );

        // 注册回复处理器
        ToastNotificationService.RegisterActionHandler("submitReply", args =>
        {
            var reply = args.GetUserInput("replyText");
            MessageBox.Show($"📨 收到回复: {reply}");
        });
    };

    // 4. 进度通知
    var progressBtn = new Button
    {
        Content = "发送进度通知",
        Width = 200,
        Height = 40
    };
    progressBtn.Click += async (s, e) =>
    {
        var data = ToastNotificationService.ShowProgress(
            "文件下载",
            "正在下载...",
            0.0,
            "0%",
            "download-001"
        );

        // 模拟进度更新
        for (int i = 0; i <= 100; i += 10)
        {
            await System.Threading.Tasks.Task.Delay(500);
            ToastNotificationService.UpdateProgress(
                data,
                i / 100.0,
                $"{i}%",
                i < 100 ? "正在下载..." : "下载完成",
                "download-001"
            );
        }
    };

    // 5. 定时通知
    var scheduleBtn = new Button
    {
        Content = "发送定时通知（5秒后）",
        Width = 200,
        Height = 40
    };
    scheduleBtn.Click += (s, e) =>
    {
        ToastNotificationService.Schedule(
            "待办提醒",
            "记得提交周报",
            DateTimeOffset.Now.AddSeconds(5),
            "todo-001",
            "work"
        );
    };

    // 6. 富媒体通知
    var richBtn = new Button
    {
        Content = "发送富媒体通知",
        Width = 200,
        Height = 40
    };
    richBtn.Click += (s, e) =>
    {
        // 注意：图片路径需要是本地路径或 ms-appx 协议
        ToastNotificationService.ShowRichNotification(
            "Andrew 发来图片",
            "查看这张美丽的风景照！",
            new Uri("D:\\Master\\Photo\\510-20240214113532309-1033428722.jpg"),  // Hero 大图
            new Uri("D:\\Master\\Photo\\3.jpg"), // 头像
            ToastGenericAppLogoCrop.Circle,
            new ToastButtonInfo("查看", "viewImage", "btnView"),
            new ToastButtonInfo("点赞", "like", "btnLike")
        );
    };

    // 7. 提醒通知（带休眠/关闭）
    var reminderBtn = new Button
    {
        Content = "发送提醒通知",
        Width = 200,
        Height = 40
    };
    reminderBtn.Click += (s, e) =>
    {
        ToastNotificationService.ShowReminder(
            "闹钟",
            "该起床了！",
            "snoozeAlarm",
            "dismissAlarm"
        );
    };

    // 8. 清除通知
    var clearBtn = new Button
    {
        Content = "清除所有通知",
        Width = 200,
        Height = 40
    };
    clearBtn.Click += (s, e) =>
    {
        ToastNotificationService.ClearAll();
    };

    // 添加所有控件到面板
    stackPanel.Children.Add(title);
    stackPanel.Children.Add(simpleBtn);
    stackPanel.Children.Add(buttonBtn);
    stackPanel.Children.Add(inputBtn);
    stackPanel.Children.Add(progressBtn);
    stackPanel.Children.Add(scheduleBtn);
    stackPanel.Children.Add(richBtn);
    stackPanel.Children.Add(reminderBtn);
    stackPanel.Children.Add(clearBtn);

    // 添加说明文本
    var infoText = new TextBlock
    {
        Text = "点击按钮发送不同类型的 Toast 通知\n通知会显示在 Windows 操作中心",
        Foreground = Brushes.Gray,
        Margin = new Thickness(0, 20, 0, 0),
        TextWrapping = TextWrapping.Wrap
    };
    stackPanel.Children.Add(infoText);

    return stackPanel;
}