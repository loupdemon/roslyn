# This script scrapes AzDo data and writes out a CSV of build runtimes
# after running this script you can open 'roslyn/artifacts/ci-times.csv' and paste into 'roslyn/scripts/all-ci-times.xlsx' to graph the data

. (Join-Path $PSScriptRoot ".." "eng" "build-utils.ps1")

$roslynPipelineId = "15"
$minDate = [DateTime]"2021-02-01"
$maxDate = [DateTime]"2021-02-08"

$baseURL = "https://dev.azure.com/dnceng/public/_apis/"
$runsURL = "$baseURL/pipelines/$roslynPipelineId/runs?api-version=6.0-preview.1"
$buildsURL = "$baseURL/build/builds/"

$helixTestJobs = @(
    "Test_Windows_Desktop_Debug_32",
    "Test_Windows_Desktop_Spanish_Debug_32",
    "Test_Windows_Desktop_Debug_64",
    "Test_Windows_CoreClr_Debug",
    "Test_Windows_Desktop_Release_32",
    "Test_Windows_Desktop_Spanish_Release_32",
    "Test_Windows_Desktop_Release_64",
    "Test_Windows_CoreClr_Release",
    "Test_Linux_Debug",
    "Test_macOS_Debug"
);

$wantedRecords = @(
    "Build_Windows_Debug",
    "Build_Windows_Release",
    "Build_Unix_Debug",

    "Correctness_Determinism",
    "Correctness_Build",
    "Correctness_SourceBuild",

    "Test_Windows_Desktop_Debug_32",
    "Test_Windows_Desktop_Spanish_Debug_32",
    "Test_Windows_Desktop_Debug_64",
    "Test_Windows_CoreClr_Debug",
    "Test_Windows_CoreClr_Debug_Single_Machine",
    "Test_Windows_Desktop_Release_32",
    "Test_Windows_Desktop_Spanish_Release_32",
    "Test_Windows_Desktop_Release_64",
    "Test_Windows_CoreClr_Release",
    "Test_Linux_Debug",
    "Test_Linux_Debug_Single_Machine",
    "Test_macOS_Debug"
)

class Job {
    [int] $runId
    [string] $name
    [string] $kind
    [TimeSpan] $duration
}

function Test-Any() {
    begin {
        $any = $false
    }
    process {
        $any = $true
    }
    end {
        $any
    }
}

function requestWithRetry($uri) {
    $i = 0
    while ($i -lt 5) {
        try {
            return Invoke-RestMethod -uri $uri
            break
        } catch {
            Write-Error "Error in request to $uri"
            Write-Error "HTTP $($_.Exception.Response.StatusCode.value__): $($_.Exception.Response.StatusDescription)"
            Write-Error "Sleeping for 5 seconds before retrying..."
            Start-Sleep -Seconds 5.0
        }
        $i++
    }
}

function initialPass() {
    $runs = requestWithRetry $runsURL
    $allJobs = [System.Collections.Generic.List[Job]]::new()

    foreach ($run in $runs.value) {
        if ($run.createdDate -lt $minDate) {
            continue
        }

        if ($run.createdDate -ge $maxDate) {
            continue
        }

        if ($run.state -ne "completed") {
            continue
        }

        if ($run.result -ne "succeeded") {
            continue
        }

        $runDetails = requestWithRetry $run._links.self.href
        $refName = $runDetails.resources.repositories.self.refName

        # uncomment the desired condition to filter the builds we measure
        if (
            # use builds from any branch
            $false

            # distrust all PR/feature/release branch builds and only get master CI builds
            # $refName -ne "refs/heads/master"

            # ignore specific PRs which modify infra and thus don't measure the "production" behavior
            # $refName -eq "refs/pulls/50046/merge" -or $refName -eq "refs/pulls/49626/merge"

            # specifically gather data on experimental builds
            # $refName -ne "refs/heads/dev/rigibson/no-windows-vmImage"
        ) {
            continue
        }

        $timeline = requestWithRetry "$buildsURL/$($run.id)/timeline"
        if ($timeline.records | Where-Object { $_.attempt -gt 1 } | Test-Any) {
            # not yet sure how to properly handle jobs with multiple attempts so will just skip them for now.
            continue
        }

        Write-Host "Measuring run $($run.id) created at $($run.createdDate) - $($run._links.web.href)"

        foreach ($record in $timeline.records) {
            if ($record.type -eq "job" -and $record.result -eq "succeeded" -and $wantedRecords.Contains($record.name)) {
                $job = [Job]::new()
                $job.runId = $run.id;
                $job.name = $record.name;
                $job.kind = if ($helixTestJobs.Contains($record.name)) { "helix-test" } else { "build" }
                $job.duration = [DateTime]$record.finishTime - [DateTime]$record.startTime;
                Write-Host "$($job.kind) job $($job.name) in run $($job.runId) took $($job.duration)"
                $allJobs.Add($job) | Out-Null
            }
        }
    }
    return $allJobs
}



$allJobs = [System.Collections.Generic.List[Job]](initialPass)
$buildTime = [TimeSpan]::Zero
$testTime = [TimeSpan]::Zero

foreach ($job in $allJobs) {
    if ($job.kind -eq "helix-test") {
        $testTime += $job.duration
    } elseif ($job.kind -eq "build") {
        $buildTime += $job.duration
    }
}

Write-Host "In date range $minDate - $maxDate (exclusive):"
Write-Host "Build time: $($buildTime.TotalHours) machine hours"
Write-Host "Helix test time: $($testTime.TotalHours) machine hours"
Write-Host "Total time: $(($buildTime + $testTime).TotalHours) machine hours"

$proportion = $testTime / ($buildTime + $testTime)
Write-Host "Proportion of machine time used to run Helix tests: $($proportion.ToString("P"))"
