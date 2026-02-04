// 消除 WPF 与 WinForms 同名的类型歧义，全局优先使用 WPF 类型
global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Button = System.Windows.Controls.Button;
global using Color = System.Windows.Media.Color;
global using Cursors = System.Windows.Input.Cursors;
global using DataFormats = System.Windows.DataFormats;
global using DragDropEffects = System.Windows.DragDropEffects;
global using DragEventArgs = System.Windows.DragEventArgs;
global using FontFamily = System.Windows.Media.FontFamily;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MessageBox = System.Windows.MessageBox;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using Orientation = System.Windows.Controls.Orientation;
global using Point = System.Windows.Point;
global using Size = System.Windows.Size;
global using UserControl = System.Windows.Controls.UserControl;
