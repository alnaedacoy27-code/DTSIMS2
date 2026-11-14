Option Strict On
Option Explicit On

''' <summary>
''' Tiny timing log: PerfLog.Mark("what just finished") appends "time since start / since last mark"
''' to %APPDATA%\DTSIMS2\perf.log, so a slow step shows up as a big number. Never throws.
''' OFF by default: it only writes when an empty file named perf.enable exists in %APPDATA%\DTSIMS2\.
''' </summary>
Public Module PerfLog

    Private ReadOnly _clock As System.Diagnostics.Stopwatch = System.Diagnostics.Stopwatch.StartNew()
    Private _last As Long = 0
    Private ReadOnly _lock As New Object()
    Private ReadOnly _enabled As Boolean = IsEnabled()

    Private Function IsEnabled() As Boolean
        Try
            Dim dir As String = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DTSIMS2")
            Return System.IO.File.Exists(System.IO.Path.Combine(dir, "perf.enable"))
        Catch
            Return False
        End Try
    End Function

    Public Sub Mark(label As String)
        If Not _enabled Then Return
        Try
            SyncLock _lock
                Dim elapsed As Long = _clock.ElapsedMilliseconds
                Dim line As String = DateTime.Now.ToString("HH:mm:ss.fff") & "  +" & (elapsed - _last).ToString().PadLeft(6) & " ms   (total " &
                                     elapsed.ToString().PadLeft(6) & " ms)   " & label
                _last = elapsed
                Dim dir As String = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DTSIMS2")
                System.IO.Directory.CreateDirectory(dir)
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "perf.log"), line & Environment.NewLine)
            End SyncLock
        Catch
            ' timing must never break the app
        End Try
    End Sub

End Module
