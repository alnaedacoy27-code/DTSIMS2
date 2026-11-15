Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

''' <summary>Stands in for a module until we build it.</summary>
<System.ComponentModel.DesignerCategory("Code")>
Public Class PlaceholderForm
    Inherits Form

    Public Sub New(title As String, description As String)
        BackColor = Color.White
        Font = Theme.FontBase

        Dim body As New Label()
        body.Dock = DockStyle.Fill
        body.TextAlign = ContentAlignment.MiddleCenter
        body.ForeColor = Theme.TextMuted
        body.Font = Theme.FontHeading
        body.Text = title & " is coming in a later step."

        Dim header As New Panel()
        header.Dock = DockStyle.Top
        header.Height = Theme.S(80)
        header.BackColor = Color.White

        Dim t As New Label()
        t.AutoSize = True
        t.Font = Theme.FontTitle
        t.ForeColor = Theme.Maroon
        t.Text = title
        t.Location = New Point(Theme.S(32), Theme.S(10))

        Dim d As New Label()
        d.AutoSize = True
        d.Font = Theme.FontBase
        d.ForeColor = Theme.TextMuted
        d.Text = description
        d.Location = New Point(Theme.S(35), Theme.S(50))

        Dim line As New Panel()
        line.Dock = DockStyle.Bottom
        line.Height = Theme.S(3)
        line.BackColor = Theme.Maroon

        header.Controls.Add(t)
        header.Controls.Add(d)
        header.Controls.Add(line)

        Controls.Add(body)      ' fill first ...
        Controls.Add(header)    ' ... then the top bar
    End Sub

End Class
