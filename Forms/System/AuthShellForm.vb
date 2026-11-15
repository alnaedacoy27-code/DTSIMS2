Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms

''' <summary>
''' Shared look of the setup / login / connection dialogs: maroon brand panel on the left,
''' white form area on the right. Fixed size, centered on screen.
''' </summary>
<System.ComponentModel.DesignerCategory("Code")>
Public Class AuthShellForm
    Inherits Form

    Protected Shared ReadOnly LeftMargin As Integer = Theme.S(56)
    Protected Shared ReadOnly FieldWidth As Integer = Theme.S(448)

    Protected ReadOnly Body As New Panel()
    Protected ReadOnly Message As New Label()
    Private ReadOnly _brand As New Panel()

    Public Sub New()
        Me.AutoScaleDimensions = New SizeF(96.0F, 96.0F)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi

        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        StartPosition = FormStartPosition.CenterScreen
        ClientSize = New Size(Theme.S(900), Theme.S(600))
        BackColor = Color.White
        Font = Theme.FontBase
        Text = "Darwin Trading - Sales and Inventory System"

        _brand.Dock = DockStyle.Left
        _brand.Width = Theme.S(340)
        _brand.BackColor = Theme.Maroon
        AddHandler _brand.Paint, AddressOf OnBrandPaint

        Body.Dock = DockStyle.Fill
        Body.BackColor = Color.White

        Message.AutoSize = False
        Message.Size = New Size(FieldWidth, Theme.S(40))
        Message.Font = Theme.FontSmall
        Message.Text = ""

        Controls.Add(Body)
        Controls.Add(_brand)
    End Sub

    ''' <summary>Big title + short explanation at the top of the form area.</summary>
    Protected Sub AddHeading(title As String, subtitle As String)
        Dim t As New Label()
        t.AutoSize = True
        t.Font = Theme.FontTitle
        t.ForeColor = Theme.TextDark
        t.Text = title
        t.Location = New Point(LeftMargin - Theme.S(3), Theme.S(30))
        Body.Controls.Add(t)

        Dim s As New Label()
        s.AutoSize = False
        s.Size = New Size(FieldWidth, Theme.S(46))
        s.Font = Theme.FontBase
        s.ForeColor = Theme.TextMuted
        s.Text = subtitle
        s.Location = New Point(LeftMargin, Theme.S(80))
        Body.Controls.Add(s)
    End Sub

    Protected Sub AddLink(link As LinkLabel, text As String, x As Integer, y As Integer)
        link.AutoSize = True
        link.Text = text
        link.Font = Theme.FontSmall
        link.LinkColor = Theme.Maroon
        link.ActiveLinkColor = Theme.MaroonDark
        link.VisitedLinkColor = Theme.Maroon
        link.LinkBehavior = LinkBehavior.HoverUnderline
        link.Location = New Point(x, y)
        link.Cursor = Cursors.Hand
        Body.Controls.Add(link)
    End Sub

    Protected Sub ShowMessage(text As String, isError As Boolean)
        Message.ForeColor = If(isError, Theme.AlertRed, Color.FromArgb(22, 130, 60))
        Message.Text = text
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        WindowStyle.ApplyMaroonTitleBar(Me)
    End Sub

    Private Shared _mark As Bitmap

    Private Sub OnBrandPaint(sender As Object, e As PaintEventArgs)
        Dim g As Graphics = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias

        Using b1 As New SolidBrush(Color.FromArgb(28, 255, 255, 255))
            g.FillEllipse(b1, -140, _brand.Height - 280, 440, 440)
        End Using
        Using b2 As New SolidBrush(Color.FromArgb(20, 255, 255, 255))
            g.FillEllipse(b2, 150, -130, 300, 300)
        End Using

        ' the DT mark above the name, on a white ring so it stands out from the maroon panel
        If _mark Is Nothing Then _mark = AppIcon.Picture(96)
        If _mark IsNot Nothing Then
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            Using ring As GraphicsPath = Theme.RoundedRect(New Rectangle(38, 90, 84, 84), 20)
                Using b As New SolidBrush(Color.FromArgb(45, 255, 255, 255))
                    g.FillPath(b, ring)
                End Using
            End Using
            g.DrawImage(_mark, New Rectangle(44, 96, 72, 72))
        End If

        TextRenderer.DrawText(g, "DARWIN", Theme.FontBrand, New Rectangle(40, 190, 290, 56), Color.White,
                              TextFormatFlags.Left Or TextFormatFlags.NoPadding)
        TextRenderer.DrawText(g, "TRADING", Theme.FontBrand, New Rectangle(40, 238, 290, 56), Color.White,
                              TextFormatFlags.Left Or TextFormatFlags.NoPadding)
        TextRenderer.DrawText(g, "Sales and Inventory System", Theme.FontMenu, New Rectangle(43, 304, 290, 28),
                              Color.FromArgb(235, 205, 205), TextFormatFlags.Left Or TextFormatFlags.NoPadding)
    End Sub

End Class
