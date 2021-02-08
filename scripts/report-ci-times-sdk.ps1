# This script scrapes AzDo data and finds the proportion of machine time spent waiting for Helix test runs to complete

. (Join-Path $PSScriptRoot ".." "eng" "build-utils.ps1")

$sdkPipelineId = "136"
$minDate = [DateTime]"2021-02-01"
$maxDate = [DateTime]"2021-02-08"

$baseURL = "https://dev.azure.com/dnceng/public/_apis/"
$runsURL = "$baseURL/pipelines/$sdkPipelineId/runs?api-version=6.0-preview.1"
$buildsURL = "$baseURL/build/builds/"

$wantedRecords = @(
    "Windows_NT Build_Debug",
    "Windows_NT Build_Release",
    
    "Windows_NT FullFramework_Build_Debug",
    "Windows_NT FullFramework_Build_Release",

    "Windows_NT TestAsTools_Build_Debug",

    # the windows jobs run agent-local and helix tests in parallel in the same task
    # linux+mac runs them in series.
    "Ubuntu_16_04 Build_Debug",
    "Ubuntu_16_04 Build_Release",

    "Darwin Build_Debug",
    "Darwin Build_Release"
)

class Job {
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
                $job.name = $record.name;
                $job.kind = "build"
                $job.duration = [DateTime]$record.finishTime - [DateTime]$record.startTime;
                Write-Host "$($job.kind) record $($job.name) took $($job.duration)"
                $allJobs.Add($job) | Out-Null
            } elseif ($record.type -eq "task" -and $record.result -eq "succeeded" -and ($record.name -eq "Run Tests in Helix" -or $record.name -eq "Run Tests in Helix and non Helix in parallel")) {
                $job = [Job]::new()
                $job.name = $record.name;
                $job.kind = "helix-test"
                $job.duration = [DateTime]$record.finishTime - [DateTime]$record.startTime;
                Write-Host "$($job.kind) record $($job.name) took $($job.duration)"
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

# here the test time is for a task and the build time is for the containing job
# the build time already accounts for the task time
$proportion = $testTime / $buildTime
Write-Host "Proportion of machine time used to run Helix tests: $($proportion.ToString("P"))"
