using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Media3D;
using System;
using MediaPlayer = Jalium.UI.Controls.MediaPlayer;

var app = new Application();

//// 创建播放器实例以便后续控制
//var mediaPlayer = new MediaPlayer
//{
//    Source = "E:\\test.mp4",  // 使用 Source 而不是 MediaPath
//    Width = 854,
//    Height = 480,
//    Stretch = Jalium.UI.Controls.Stretch.Uniform,
//    LoadedBehavior = MediaState.Manual
//};

//mediaPlayer.Play();

// 订阅事件
//mediaPlayer.MediaOpened += (s, e) =>
//    MessageBox.Show($"Media opened: {mediaPlayer.NaturalVideoWidth}x{mediaPlayer.NaturalVideoHeight}");

//mediaPlayer.MediaEnded += (s, e) =>
//     MessageBox.Show("Playback completed");

//mediaPlayer.MediaFailed += (s, e) =>
//    MessageBox.Show($"Error: {e.ErrorMessage}");

//// 控制按钮
//var playBtn = new Button { Content = "Play" };
//playBtn.Click += (s, e) => mediaPlayer.Play();

//var pauseBtn = new Button { Content = "Pause" };
//pauseBtn.Click += (s, e) => mediaPlayer.Pause();

//var stopBtn = new Button { Content = "Stop" };
//stopBtn.Click += (s, e) => mediaPlayer.Stop();

//var loadBtn = new Button { Content = "Load Video" };
//loadBtn.Click += (s, e) =>
//{
//    // 动态加载媒体
//    mediaPlayer.MediaPath = "E:/test.mp4";
//};

//// 音量滑块
//var volumeSlider = new Slider { Minimum = 0, Maximum = 1, Value = 0.8 };
//volumeSlider.ValueChanged += (s, e) =>
//    mediaPlayer.Volume = volumeSlider.Value;

// 布局
var window = new Window
{
    Title = "Media Player",
    Width = 1000,
    Height = 700,
    Content = new StackPanel
    {
        Margin = new Thickness(20),
        Children =
        {
            new MediaPlayer
{
    Source = "test.mp4",
    Width = 700,
    Height = 700,
    Stretch = Jalium.UI.Controls.Stretch.Uniform,
    LoadedBehavior = MediaState.Play
}
            
            //// 控制栏
            //new StackPanel
            //{
            //    Orientation = Orientation.Horizontal,
            //    Children = { playBtn, pauseBtn, stopBtn, loadBtn }
            //},
            
            //// 信息显示
            //new TextBlock { Name = "info", Text = "No media loaded" }
        }
    }
};

app.Run(window);