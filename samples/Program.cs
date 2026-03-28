using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using System;
using MediaPlayer = Jalium.UI.Controls.MediaElement;
using Stretch = Jalium.UI.Controls.Stretch;

var app = new Application();

// 创建播放器
var mediaPlayer = new MediaPlayer
{
    Source = new Uri("file:///D:/test.mp4"),
    Width = 854,
    Height = 480,
    Stretch = Stretch.Fill,
    LoadedBehavior = MediaState.Manual
};

// 事件
mediaPlayer.MediaOpened += (s, e) => Console.WriteLine($"Opened: {mediaPlayer.NaturalVideoWidth}x{mediaPlayer.NaturalVideoHeight}");
mediaPlayer.MediaEnded += (s, e) => Console.WriteLine("Ended");
mediaPlayer.MediaFailed += (s, e) => Console.WriteLine($"Failed: {e.ErrorMessage}");

// 控制按钮
var playBtn = new Button { Content = "▶ Play", Width = 80 };
playBtn.Click += (s, e) => mediaPlayer.Play();

var pauseBtn = new Button { Content = "⏸ Pause", Width = 80 };
pauseBtn.Click += (s, e) => mediaPlayer.Pause();

var stopBtn = new Button { Content = "⏹ Stop", Width = 80 };
stopBtn.Click += (s, e) => mediaPlayer.Stop();

// 简单布局：视频 + 按钮行
var panel = new StackPanel
{
    Children =
    {
        mediaPlayer,
        new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(10),
            Children = { playBtn, pauseBtn, stopBtn }
        }
    },
    
};

var window = new Window
{
    Title = "Video Player",
    Width = 900,
    Height = 600,
    Content = panel
};

app.Run(window);