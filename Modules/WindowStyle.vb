Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Maroon title bar on Windows 11 (silently ignored on older Windows) and the DT icon on every window.</summary>
Public Module WindowStyle

    Private Declare Function DwmSetWindowAttribute Lib "dwmapi.dll" (hwnd As IntPtr, attr As Integer, ByRef attrValue As Integer, attrSize As Integer) As Integer

    Public Sub ApplyMaroonTitleBar(frm As Form)
        Dim ico As Icon = AppIcon.Current
        If ico IsNot Nothing Then frm.Icon = ico
        Try
            Dim caption As Integer = &H80      ' COLORREF 0x00BBGGRR  -> RGB(128,0,0)
            Dim textWhite As Integer = &HFFFFFF
            DwmSetWindowAttribute(frm.Handle, 35, caption, 4)    ' caption color
            DwmSetWindowAttribute(frm.Handle, 34, caption, 4)    ' border color
            DwmSetWindowAttribute(frm.Handle, 36, textWhite, 4)  ' caption text color
        Catch
            ' not supported on this Windows version - harmless
        End Try
    End Sub

End Module

''' <summary>
''' The DT icon ("DT icon.ico", embedded in the exe): maroon tile, white DT. It uses the same maroon as Theme,
''' so the title bars, the taskbar and the screens all match.
''' </summary>
Public Module AppIcon

    Private _icon As Icon
    Private _loaded As Boolean

    ''' <summary>The icon with every size (16 to 256), or Nothing if it cannot be read.</summary>
    Public ReadOnly Property Current As Icon
        Get
            If Not _loaded Then
                _loaded = True
                Try
                    Using s As IO.Stream = GetType(AppIcon).Assembly.GetManifestResourceStream("DTSIMS2.AppIcon.ico")
                        If s IsNot Nothing Then _icon = New Icon(s)
                    End Using
                Catch
                    _icon = Nothing
                End Try
            End If
            Return _icon
        End Get
    End Property

    ''' <summary>
    ''' The mark as a picture: the smallest image in the icon that is at least <paramref name="size"/> wide
    ''' (caller disposes it). Read straight from the .ico, because Icon.ToBitmap cannot read the PNG images inside it.
    ''' </summary>
    Public Function Picture(size As Integer) As Bitmap
        Try
            Dim bytes As Byte()
            Using s As IO.Stream = GetType(AppIcon).Assembly.GetManifestResourceStream("DTSIMS2.AppIcon.ico")
                If s Is Nothing Then Return Nothing
                Using ms As New IO.MemoryStream()
                    s.CopyTo(ms)
                    bytes = ms.ToArray()
                End Using
            End Using
            Dim count As Integer = BitConverter.ToUInt16(bytes, 4)
            Dim bestW As Integer = -1, bestOffset As Integer = 0, bestLen As Integer = 0
            For i As Integer = 0 To count - 1
                Dim e As Integer = 6 + 16 * i
                Dim w As Integer = If(bytes(e) = 0, 256, CInt(bytes(e)))
                Dim better As Boolean = bestW < 0 OrElse
                    (w >= size AndAlso (bestW < size OrElse w < bestW)) OrElse
                    (w < size AndAlso bestW < size AndAlso w > bestW)
                If better Then
                    bestW = w
                    bestLen = BitConverter.ToInt32(bytes, e + 8)
                    bestOffset = BitConverter.ToInt32(bytes, e + 12)
                End If
            Next
            If bestW < 0 Then Return Nothing
            Using img As New IO.MemoryStream(bytes, bestOffset, bestLen)
                Using raw As Image = Image.FromStream(img)
                    Return New Bitmap(raw)
                End Using
            End Using
        Catch
            Return Nothing
        End Try
    End Function

End Module
