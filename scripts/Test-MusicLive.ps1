param([Parameter(Mandatory=$true)][string]$HostExe,[Parameter(Mandatory=$true)][string]$PlaylistUrl,[int]$NextCount=6,[ValidatePattern('^$|^[A-Za-z0-9_-]{11}$')][string]$RepeatTrackId='')
$ErrorActionPreference='Stop'
$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')
$profile=Join-Path $tempRoot ('IslandMusicVerification-'+[guid]::NewGuid().ToString('N'))
$start=[Diagnostics.ProcessStartInfo]::new($HostExe)
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardInput=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
$start.WorkingDirectory=Split-Path $HostExe;$start.Environment['ISLAND_MUSIC_VERIFICATION_PROFILE']=$profile
$process=$null;$lastState=$null;$nextRead=$null;$requestId=0;$seen=[Collections.Generic.List[string]]::new()
function Send([string]$Kind,[double]$Value=0){
 $script:requestId++;$request=@{id=$script:requestId;kind=$Kind;generation=1;value=$Value;expires=[DateTimeOffset]::UtcNow.AddSeconds(3).ToUnixTimeMilliseconds()}
 if($Kind -eq 'Open'){$request.url=$PlaylistUrl}
 $process.StandardInput.WriteLine(($request|ConvertTo-Json -Compress));$process.StandardInput.Flush();return $script:requestId
}
function Read-Until([scriptblock]$Condition,[int]$Seconds=30){
 $deadline=[DateTimeOffset]::UtcNow.AddSeconds($Seconds)
 while([DateTimeOffset]::UtcNow -lt $deadline){
  if($process.HasExited){throw 'Player host exited before verification.'}
  if($null -eq $script:nextRead){$script:nextRead=$process.StandardOutput.ReadLineAsync()}
  if(!$script:nextRead.Wait(500)){continue}
  $line=$script:nextRead.Result;$script:nextRead=$null;if($null -eq $line){throw 'Player pipe closed.'}
  $message=$line|ConvertFrom-Json
  if($message.type -eq 'state'){$script:lastState=$message.state}
  if(& $Condition $message){return $message}
 }
 Write-Host ('TIMEOUT STATE: '+($script:lastState|ConvertTo-Json -Depth 4 -Compress))
 throw 'Timed out waiting for actual YouTube playback state.'
}
function Command([string]$Kind,[double]$Value=0){
 if($Kind -in 'Next','Previous'){
  # Use the same readiness conditions as the physical island transport buttons.
  Read-Until {param($m) $m.type -eq 'state' -and $m.state.ready -and !$m.state.switching -and !$m.state.ad -and $m.state.count -gt 1 -and !$m.state.switching} 30 | Out-Null
 }
 $id=Send $Kind $Value
 $ack=Read-Until {param($m) $m.type -eq 'ack' -and $m.id -eq $id} 12
 if(!$ack.ok){Write-Output ('REJECTED: '+$Kind+' ready='+$lastState.ready+' ad='+$lastState.ad+' count='+$lastState.count+' pending='+$lastState.queue.pending);throw "Playback command rejected: $Kind"}
}
try{
 $process=[Diagnostics.Process]::Start($start);$stderr=$process.StandardError.ReadToEndAsync()
 Read-Until {param($m) $m.type -eq 'host-ready'} 30 | Out-Null
 Send 'Open' | Out-Null
 Read-Until {param($m) $m.type -eq 'state' -and $m.state.ready -and $m.state.count -gt 1 -and !$m.state.ad} 90 | Out-Null
 Write-Output ('READY: '+$lastState.title+'; loaded playlist count='+$lastState.count)
 $knownPlaylistCount=$lastState.count
 $seen.Add($lastState.video);Command 'Shuffle' 1
 Read-Until {param($m) $m.type -eq 'state' -and $m.state.ready -and $m.state.shuffle} 15 | Out-Null
 for($i=0;$i -lt $NextCount;$i++){
  $before=$lastState.video;Command 'Next'
  Read-Until {param($m) $m.type -eq 'state' -and $m.state.ready -and !$m.state.switching -and !$m.state.ad -and $m.state.video -ne $before} 45 | Out-Null
  if(!$lastState.shuffle){throw 'Shuffle was lost after a track change.'}
  $seen.Add($lastState.video);Write-Output ('NEXT '+($i+1)+': '+$lastState.video+' '+$lastState.title+'; shuffle='+$lastState.shuffle)
 }
 if(($seen|Select-Object -Unique).Count -ne $seen.Count){throw 'Random queue repeated a track before completing this sample.'}
 $previous=$seen[$seen.Count-2];Command 'Previous'
 Read-Until {param($m) $m.type -eq 'state' -and $m.state.ready -and $m.state.video -eq $previous} 45 | Out-Null
 Command 'Next';$last=$seen[$seen.Count-1]
 Read-Until {param($m) $m.type -eq 'state' -and $m.state.ready -and $m.state.video -eq $last} 45 | Out-Null
 $remaining=@($lastState.queue.bag);$beforeEnd=$lastState.video
 Command 'Seek' ([Math]::Max(0,$lastState.duration-1))
 Read-Until {param($m) $m.type -eq 'state' -and $m.state.ready -and !$m.state.switching -and !$m.state.ad -and $m.state.video -ne $beforeEnd} 45 | Out-Null
 if(!$lastState.shuffle -or $lastState.video -notin $remaining){Write-Output ('END DIAGNOSTIC: before='+$beforeEnd+' actual='+$lastState.video+' remaining='+($remaining -join ',')+' queue='+($lastState.queue|ConvertTo-Json -Compress));throw 'Actual ended event did not use the remaining shuffled queue.'}
 Write-Output ('AUTO NEXT: '+$lastState.video+' '+$lastState.title+'; shuffle='+$lastState.shuffle)
 if($RepeatTrackId){
  # SPA transitions can temporarily report count=0 even when the video is ready.
  # Keep the finite bound from the fully loaded initial playlist.
  $maxTrackHops=[Math]::Min(200,$knownPlaylistCount*2)
  for($i=0;$lastState.video -ne $RepeatTrackId -and $i -lt $maxTrackHops;$i++){
   $current=$lastState.video;Command 'Next'
   Read-Until {param($m) $m.type -eq 'state' -and $m.state.ready -and !$m.state.switching -and !$m.state.ad -and $m.state.video -ne $current} 45 | Out-Null
  }
  if($lastState.video -ne $RepeatTrackId){throw 'Requested repeat regression track was not found in the playlist.'}
  Write-Output ('REPEAT REGRESSION TRACK: '+$lastState.video+' '+$lastState.title)
 }
 Command 'Repeat' 2
 Write-Output 'CHECK: single-track repeat enabled.'
 Read-Until {param($m) $m.type -eq 'state' -and $m.state.repeat -eq 2} 15 | Out-Null
 $loopVideo=$lastState.video;Command 'Seek' ([Math]::Max(0,$lastState.duration-1))
 Write-Output ('CHECK: loop seek video='+$loopVideo+' duration='+$lastState.duration+' playing='+$lastState.playing)
 Read-Until {param($m) $m.type -eq 'state' -and $m.state.ready -and $m.state.video -eq $loopVideo -and $m.state.position -lt 10 -and $m.state.playing} 20 | Out-Null
 Command 'Repeat' 0
 Read-Until {param($m) $m.type -eq 'state' -and $m.state.repeat -eq 0 -and $m.state.shuffle} 15 | Out-Null
 Write-Output 'PASS: actual end-of-track follows the shuffled queue; single-track repeat loops; disabling repeat retains shuffle.'
 Write-Output ('PASS: '+$seen.Count+' unique actual tracks; shuffle persisted; Previous/Next history preserved. Muted isolated profile; no Gemini calls.')
}finally{
 if($process){try{$process.StandardInput.Close();if(!$process.WaitForExit(5000)){$process.Kill($true)}}finally{$process.Dispose()}}
 # Retain the isolated profile until WebView2 finishes its asynchronous teardown.
 Write-Output ('Verification profile: '+$profile)
}
