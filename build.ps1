$fw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$md = "C:\Windows\System32\WinMetadata"
& "$fw\csc.exe" /nologo /target:winexe /optimize /codepage:65001 /out:"$PSScriptRoot\NetMeter.exe" /win32icon:"$PSScriptRoot\icon.ico" `
  /r:System.Windows.Forms.dll /r:System.Drawing.dll `
  /r:"$fw\WPF\UIAutomationClient.dll" /r:"$fw\WPF\UIAutomationTypes.dll" /r:"$fw\WPF\WindowsBase.dll" `
  /r:"$fw\System.Runtime.dll" /r:"$fw\System.Runtime.WindowsRuntime.dll" `
  /r:"$md\Windows.Foundation.winmd" /r:"$md\Windows.Networking.winmd" `
  "$PSScriptRoot\NetMeter.cs" "$PSScriptRoot\SettingsForm.cs" "$PSScriptRoot\Demo.cs"

