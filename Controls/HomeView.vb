Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Welcome screen with a Menu / Dashboard switch (remembered on this computer).
'''   Menu:      a tile for every module the logged-in person may open.
'''   Dashboard: today at a glance (DashboardView), refreshed by itself when the data changes.
''' </summary>
Public Class HomeView
    Inherits Panel
    Implements ILiveForm

    Public Event ModuleRequested(info As ModuleInfo)

    Private Const ViewSetting As String = "home_view"

    Private ReadOnly _header As New Panel()
    Private ReadOnly _hello As New Label()
    Private ReadOnly _subtitle As New Label()
    Private ReadOnly _switch As New PillTabs()
    Private ReadOnly _updated As New Label()
    Private ReadOnly _host As New FlowLayoutPanel()
    Private ReadOnly _groupFlows As New List(Of FlowLayoutPanel)()
    Private ReadOnly _timer As New Timer()
    Private _dash As DashboardView

    Public Sub New(fullName As String, role As String, modules As List(Of ModuleInfo))
        Dock = DockStyle.Fill
        BackColor = Color.White

        ' ---------------------------------------------------------------- header: greeting + switch
        _header.Dock = DockStyle.Top
        _header.Height = Theme.S(84)
        _header.BackColor = Color.White

        _hello.AutoSize = True
        _hello.Font = Theme.FontTitle
        _hello.ForeColor = Theme.TextDark
        _hello.Text = "Welcome back, " & fullName
        _hello.Location = New Point(Theme.S(33), Theme.S(14))

        _subtitle.AutoSize = True
        _subtitle.Font = Theme.FontBase
        _subtitle.ForeColor = Theme.TextMuted
        _subtitle.Text = Permissions.RoleDisplay(role) & "  |  " & DateTime.Now.ToString("dddd, MMMM d, yyyy")
        _subtitle.Location = New Point(Theme.S(36), Theme.S(54))

        _switch.SetItems("Menu", "Dashboard")
        _updated.AutoSize = False
        _updated.TextAlign = ContentAlignment.MiddleRight
        _updated.Font = Theme.FontSmall
        _updated.ForeColor = Theme.TextMuted
        _updated.Size = New Size(Theme.S(200), Theme.S(20))

        _header.Controls.AddRange(New Control() {_hello, _subtitle, _switch, _updated})
        AddHandler _header.Resize, Sub(s, e) PlaceSwitch()

        ' ---------------------------------------------------------------- menu tiles
        _host.Dock = DockStyle.Fill
        _host.FlowDirection = FlowDirection.TopDown
        _host.WrapContents = False
        _host.AutoScroll = True
        _host.Padding = New Padding(Theme.S(36), Theme.S(4), Theme.S(24), Theme.S(24))
        _host.BackColor = Color.White

        If modules.Count = 0 Then
            Dim none As New Label()
            none.AutoSize = True
            none.Font = Theme.FontBase
            none.ForeColor = Theme.TextMuted
            none.Text = "There are no modules available for this account."
            _host.Controls.Add(none)
        End If

        For Each groupName As String In ModuleCatalog.GroupOrder
            Dim flow As FlowLayoutPanel = Nothing

            For Each m As ModuleInfo In modules
                If m.Group <> groupName Then Continue For

                If flow Is Nothing Then
                    Dim header As New Label()
                    header.AutoSize = True
                    header.Font = Theme.FontHeading
                    header.ForeColor = Theme.Maroon
                    header.Text = groupName
                    header.Margin = New Padding(0, Theme.S(8), 0, Theme.S(8))
                    _host.Controls.Add(header)

                    flow = New FlowLayoutPanel()
                    flow.AutoSize = True
                    flow.AutoSizeMode = AutoSizeMode.GrowAndShrink
                    flow.WrapContents = True
                    flow.FlowDirection = FlowDirection.LeftToRight
                    flow.BackColor = Color.White
                    flow.Margin = New Padding(0, 0, 0, Theme.S(4))
                    _host.Controls.Add(flow)
                    _groupFlows.Add(flow)
                End If

                Dim tile As New TileButton()
                tile.Title = m.Title
                tile.Subtitle = m.Description
                tile.Tag = m
                tile.Margin = New Padding(0, 0, Theme.S(16), Theme.S(16))
                AddHandler tile.Click, AddressOf OnTileClick
                flow.Controls.Add(tile)
            Next
        Next

        AddHandler _host.SizeChanged, AddressOf UpdateFlowWidths
        UpdateFlowWidths(Me, EventArgs.Empty)

        Controls.Add(_host)
        Controls.Add(_header)

        ' the figures also change with time (who is online, "today"): read them again every minute
        _timer.Interval = 60000
        AddHandler _timer.Tick, Sub(s, e) If _dash IsNot Nothing AndAlso _dash.Visible Then _dash.RefreshData()

        _switch.SelectedIndex = If(String.Equals(LocalDevices.GetValue(ViewSetting), "dashboard", StringComparison.OrdinalIgnoreCase), 1, 0)
        AddHandler _switch.SelectedIndexChanged, AddressOf OnSwitch
        ShowView()
    End Sub

    ' ==================================================================== switching
    Private ReadOnly Property DashboardShown As Boolean
        Get
            Return _switch.SelectedIndex = 1
        End Get
    End Property

    Private Sub OnSwitch(sender As Object, e As EventArgs)
        Try
            LocalDevices.SetValue(ViewSetting, If(DashboardShown, "dashboard", "menu"))
        Catch
            ' the choice is only a convenience
        End Try
        ShowView()
    End Sub

    Private Sub ShowView()
        If DashboardShown Then
            If _dash Is Nothing Then
                _dash = New DashboardView()
                AddHandler _dash.OpenRequested, AddressOf OnOpenRequested
                AddHandler _dash.Loaded, Sub(at As DateTime) _updated.Text = "Updated " & at.ToString("h:mm tt")
                Controls.Add(_dash)
                _dash.BringToFront()
            Else
                _dash.Visible = True
                _dash.BringToFront()
                _dash.RefreshData()
            End If
            _host.Visible = False
            _updated.Visible = True
            _timer.Start()
        Else
            If _dash IsNot Nothing Then _dash.Visible = False
            _host.Visible = True
            _host.BringToFront()
            _updated.Visible = False
            _timer.Stop()
        End If
        PlaceSwitch()
    End Sub

    Private Sub PlaceSwitch()
        Dim right As Integer = _header.ClientSize.Width - Theme.S(24)
        _switch.Location = New Point(right - _switch.Width, Theme.S(16))
        _updated.Location = New Point(right - _updated.Width, _switch.Bottom + Theme.S(4))
    End Sub

    Private Sub OnOpenRequested(moduleKey As String)
        For Each m As ModuleInfo In ModuleCatalog.All
            If m.Key = moduleKey Then
                RaiseEvent ModuleRequested(m)
                Return
            End If
        Next
    End Sub

    ' ==================================================================== live updates (from the main window)
    Public ReadOnly Property WatchedTables As String() Implements ILiveForm.WatchedTables
        Get
            Return New String() {"sales", "sale_items", "products", "deliveries", "users"}
        End Get
    End Property

    Public Sub RefreshData() Implements ILiveForm.RefreshData
        If _dash IsNot Nothing AndAlso _dash.Visible Then _dash.RefreshData()
    End Sub

    ' ==================================================================== menu tiles
    ' keep the tile rows wrapping to the window width, and stretch the tiles so a wide
    ' screen is filled without going overboard (max 4 per row, each at most 400 wide)
    Private Sub UpdateFlowWidths(sender As Object, e As EventArgs)
        Dim w As Integer = _host.ClientSize.Width - _host.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth
        w = Math.Max(300, w)

        Dim cols As Integer = Math.Max(1, Math.Min(4, w \ Theme.S(276)))
        Dim tileW As Integer = Math.Max(Theme.S(250), Math.Min(Theme.S(400), w \ cols - Theme.S(16)))

        For Each f As FlowLayoutPanel In _groupFlows
            f.SuspendLayout()
            f.MaximumSize = New Size(w, 0)
            For Each c As Control In f.Controls
                If TypeOf c Is TileButton Then c.Size = New Size(tileW, Theme.S(108))
            Next
            f.ResumeLayout()
        Next
    End Sub

    Private Sub OnTileClick(sender As Object, e As EventArgs)
        Dim t As TileButton = TryCast(sender, TileButton)
        If t Is Nothing Then Return
        Dim info As ModuleInfo = TryCast(t.Tag, ModuleInfo)
        If info IsNot Nothing Then RaiseEvent ModuleRequested(info)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _timer.Stop()
            _timer.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

End Class
