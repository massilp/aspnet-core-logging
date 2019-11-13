# Checks whether a given Docker container is running or not.

Param (
    # Represents the name of the Docker container to check whtether is running.
    $ContainerName,

    # Represents the number of milliseconds to wait before checking again whether the given container is running.
    $SleepingTimeInMillis = 1000,

    # The maximum amount of retries before giving up and considering that the given Docker container is not running.
    $MaxNumberOfTries = 30
)

$ErrorActionPreference = 'Stop'

$numberOfTries = 1
$hasContainerStarted = $false

Do {
    Start-Sleep -Milliseconds $sleepingTimeInMillis

    $inspectOutput = docker inspect $ContainerName | ConvertFrom-Json 
    $containerDetails = $inspectOutput[0]
    $containerStatus = $containerDetails.State.Status

    if ($containerStatus -eq 'running') {
        Write-Output "Container ""$ContainerName"" is running"
        $hasContainerStarted = $true
        break
    }

    Write-Output "#${numberOfTries}: Container ""$ContainerName"" isn't running yet; will check again in $sleepingTimeInMillis milliseconds"
    $numberOfTries++
}
Until ($numberOfTries -gt $maxNumberOfTries)

if (!$hasContainerStarted) {
        Write-Output "##vso[task.LogIssue type=error;] Container $ContainerName is not running yet; will stop here"
        Write-Output "##vso[task.complete result=Failed;]"
}