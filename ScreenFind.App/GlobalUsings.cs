// WinForms is referenced for the tray icon only, and its implicit usings collide with WPF on
// several very common type names. These aliases pin every ambiguous name to its WPF meaning;
// TrayIcon.cs is the one file that deliberately reaches for the WinForms types.

global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Color = System.Windows.Media.Color;
global using ColorConverter = System.Windows.Media.ColorConverter;
global using FlowDirection = System.Windows.FlowDirection;
global using Point = System.Windows.Point;
global using Size = System.Windows.Size;
global using Canvas = System.Windows.Controls.Canvas;
global using CheckBox = System.Windows.Controls.CheckBox;
global using ComboBox = System.Windows.Controls.ComboBox;
global using ContextMenu = System.Windows.Controls.ContextMenu;
global using Label = System.Windows.Controls.Label;
global using Orientation = System.Windows.Controls.Orientation;
global using Rectangle = System.Windows.Shapes.Rectangle;
global using Clipboard = System.Windows.Clipboard;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MessageBox = System.Windows.MessageBox;
global using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
global using TextBox = System.Windows.Controls.TextBox;
