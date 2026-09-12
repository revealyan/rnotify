param([string]$Title, [string]$Body)
[void][Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime]
[void][Windows.UI.Notifications.ToastNotification,Windows.UI.Notifications,ContentType=WindowsRuntime]
[void][Windows.Data.Xml.Dom.XmlDocument,Windows.Data.Xml.Dom,ContentType=WindowsRuntime]
$x = New-Object Windows.Data.Xml.Dom.XmlDocument
$esc = $Title -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;'
$escB = $Body -replace '&','&amp;' -replace '<','&lt;' -replace '>','&gt;'
$x.LoadXml("<toast><visual><binding template=`"ToastGeneric`"><text>$esc</text><text>$escB</text></binding></visual></toast>")
$t = New-Object Windows.UI.Notifications.ToastNotification($x)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe').Show($t)
"sent: $Title"
