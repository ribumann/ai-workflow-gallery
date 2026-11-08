<#
.SYNOPSIS
    Smoke-tests the gallery's MCP server end to end.

.DESCRIPTION
    Speaks JSON-RPC to /api/mcp exactly as an MCP client would: initialize, tools/list, a search,
    and a get on whatever the search returned. Use it to prove the endpoint works *before* wiring an
    agent up, so that when the agent misbehaves you already know whether the server is the problem.

    Use a delegated bearer token obtained by an explicitly registered Entra client. Prefer
    integrations/reference-client for provider-managed sign-in and an in-memory token cache.

.PARAMETER AccessToken
    A delegated API access token as a SecureString. Never paste it into a command or config.

.PARAMETER BaseUrl
    Explicit gallery origin to test. No production or legacy environment default.

.PARAMETER ExpectWrites
    Assert that the write tools are advertised, and exercise one create/update/delete round-trip
    against a throwaway report link. Without this switch the script asserts the opposite — that
    every advertised tool declares itself read-only — so it can prove the FR-007 gate is shut in an
    environment where writes are meant to be off.

.PARAMETER Anonymous
    Explicit public read-only mode. No Entra discovery/token or private-auth fallback.

.PARAMETER CollectionId
    Explicit public-techniques, curated-tools or playground collection in either authentication mode.

.EXAMPLE
    ./scripts/test-mcp-server.ps1 -AccessToken (Read-Host -AsSecureString) -BaseUrl https://<uat-host> -CollectionId public-techniques

.EXAMPLE
    ./scripts/test-mcp-server.ps1 -Anonymous -BaseUrl https://<approved-public-host> -CollectionId public-techniques
#>
[CmdletBinding(DefaultParameterSetName = 'Entra', SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Entra')]
    [Security.SecureString]$AccessToken,

    [Parameter(Mandatory = $true, ParameterSetName = 'Anonymous')]
    [switch]$Anonymous,

    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [ValidateSet('public-techniques', 'curated-tools', 'playground')]
    [string]$CollectionId,

    [Parameter(ParameterSetName = 'Entra')]
    [switch]$ExpectWrites
)

$ErrorActionPreference = 'Stop'

if ($CollectionId -cnotin @('public-techniques', 'curated-tools', 'playground')) {
    throw 'CollectionId must exactly match public-techniques, curated-tools or playground.'
}
$endpoint = "$($BaseUrl.TrimEnd('/'))/api/mcp"
$uri = [Uri]$BaseUrl
if ($uri.Scheme -ne 'https' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/') {
    throw 'BaseUrl must be an explicit HTTPS gallery origin without credentials or query.'
}
if ($PSCmdlet.ParameterSetName -eq 'Anonymous' -and -not $Anonymous) {
    throw 'Anonymous mode must be explicitly selected.'
}
if ($ExpectWrites -and -not $PSCmdlet.ShouldProcess(
    "$endpoint collection $CollectionId",
    'Create a throwaway report, update it, test a stale-version conflict and delete it')) {
    throw 'Write verification was not approved; no requests were sent.'
}
$requestId = 0
$failures = 0

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(60)
if (-not $Anonymous) {
    $plainToken = [Net.NetworkCredential]::new('', $AccessToken).Password
    $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $plainToken)
    $plainToken = $null
}

function Invoke-Rpc {
    param([string]$Method, [hashtable]$Params)

    $script:requestId++
    if ($Method -eq 'tools/call') {
        $Params.arguments.collectionId = $CollectionId
    }
    $envelope = @{ jsonrpc = '2.0'; id = $script:requestId; method = $Method }
    if ($Params) { $envelope.params = $Params }

    $body = [System.Net.Http.StringContent]::new(
        ($envelope | ConvertTo-Json -Depth 10), [System.Text.Encoding]::UTF8, 'application/json')
    $response = $client.PostAsync($endpoint, $body).Result
    $text = $response.Content.ReadAsStringAsync().Result

    if ([int]$response.StatusCode -eq 401) {
        throw 'Bearer authentication was rejected. Reauthenticate with the registered delegated client.'
    }
    if ([int]$response.StatusCode -eq 404) {
        throw "The MCP server is not enabled at the selected endpoint. Stop and ask the owner."
    }
    if (-not $response.IsSuccessStatusCode) {
        throw "HTTP $([int]$response.StatusCode) from $endpoint : $text"
    }

    $payload = $text | ConvertFrom-Json

    # JSON-RPC errors come back as HTTP 200 with an `error` member and no `result`. Without this
    # check the caller sees a null result and blames whatever it was asserting, which is exactly
    # the confidently-wrong diagnosis this script exists to avoid.
    if ($payload.PSObject.Properties.Name -contains 'error' -and $payload.error) {
        throw "JSON-RPC error $($payload.error.code) from '$Method': $($payload.error.message)"
    }

    return $payload
}

function Test-Step {
    param([string]$Name, [scriptblock]$Body)

    Write-Host "`n=== $Name ===" -ForegroundColor Cyan
    try {
        $outcome = & $Body
        if ($outcome -eq 'skipped') {
            Write-Host "SKIPPED" -ForegroundColor Yellow
        }
        else {
            Write-Host "PASS" -ForegroundColor Green
        }
    }
    catch {
        Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
        $script:failures++

        # A session problem breaks every step identically; repeating it four times just buries the
        # one line that matters. But bailing out mid-write-round-trip would leave the throwaway
        # entry behind in a shared gallery, so say what was orphaned before going.
        if ($_.Exception.Message -like 'Not signed in*' -or $_.Exception.Message -like '*not enabled*') {
            Write-Host "`nStopping: nothing else can succeed until this is fixed." -ForegroundColor Red
            if ($script:createdReportId) {
                Write-Host "A smoke-test entry was left behind and must be deleted by hand:" -ForegroundColor Yellow
                Write-Host "  $($BaseUrl.TrimEnd('/'))/reports/$($script:createdReportId)" -ForegroundColor Yellow
            }
            exit 1
        }
    }
}

Write-Host "Testing $endpoint"

$firstTaskId = $null

Test-Step 'initialize' {
    $result = (Invoke-Rpc 'initialize' @{
            protocolVersion = '2026-07-28'
            clientInfo      = @{ name = 'test-mcp-server.ps1'; version = '1.0' }
            capabilities    = @{}
        }).result

    if (-not $result) { throw 'No result member in the response.' }
    Write-Host "  server:   $($result.serverInfo.name) $($result.serverInfo.version)"
    Write-Host "  protocol: $($result.protocolVersion)"
}

Test-Step 'tools/list' {
    $tools = (Invoke-Rpc 'tools/list').result.tools
    if (-not $tools) { throw 'The catalog is empty.' }

    Write-Host "  $($tools.Count) tools: $(($tools | ForEach-Object { $_.name }) -join ', ')"

    # A tool that declares no readOnlyHint at all is treated as suspect rather than as safe — the
    # absence of the claim is not the claim.
    $writeTools = $tools | Where-Object { $_.annotations.readOnlyHint -ne $true }

    if (-not $ExpectWrites) {
        if ($writeTools) {
            throw "Expected every tool to declare itself read-only, but these do not: $(($writeTools | ForEach-Object { $_.name }) -join ', ')"
        }
        $expectedReads = @('search_tasks', 'get_task', 'search_workflows', 'get_workflow',
            'search_methods', 'get_method', 'search_reports', 'get_report')
        $missingReads = $expectedReads | Where-Object { $_ -notin $tools.name }
        if ($tools.Count -ne 8 -or $missingReads) { throw 'Expected exactly the eight gallery read tools.' }
        return
    }

    # Twelve write tools: four content types (task, workflow, method, report) x create/update/delete.
    # Counting them is what catches a half-deployed build, where the gate is on but the API predates
    # some of the tools.
    $expected = @(
        'create_task', 'update_task', 'delete_task',
        'create_workflow', 'update_workflow', 'delete_workflow',
        'create_method', 'update_method', 'delete_method',
        'create_report', 'update_report', 'delete_report')
    $missing = $expected | Where-Object { $_ -notin ($tools | ForEach-Object { $_.name }) }
    if ($missing) {
        throw "EnableMcpWrites is on but these tools are missing: $($missing -join ', ')"
    }
    Write-Host "  $($writeTools.Count) write tool(s) advertised"
}

Test-Step 'search_tasks' {
    $result = (Invoke-Rpc 'tools/call' @{ name = 'search_tasks'; arguments = @{ pageSize = 5 } }).result
    if ($result.isError) { throw $result.content[0].text }

    $page = $result.content[0].text | ConvertFrom-Json
    Write-Host "  $($page.totalCount) task(s) in the catalog; showing $($page.items.Count)"

    if ($page.items.Count -gt 0) {
        $script:firstTaskId = $page.items[0].id
        Write-Host "  first: $($page.items[0].name)"
        Write-Host "  link:  $($page.items[0].galleryUrl)"

        if (-not $page.items[0].galleryUrl) {
            throw 'Results carry no galleryUrl back-link - check the GalleryBaseUrl app setting.'
        }
    }
    else {
        Write-Host '  (catalog is empty - get_task will be skipped)' -ForegroundColor Yellow
    }
}

Test-Step 'get_task' {
    if (-not $firstTaskId) { return 'skipped' }

    $result = (Invoke-Rpc 'tools/call' @{ name = 'get_task'; arguments = @{ id = $firstTaskId } }).result
    if ($result.isError) { throw $result.content[0].text }

    $task = $result.content[0].text | ConvertFrom-Json
    Write-Host "  $($task.name) [$($task.taskType)]"
}

Test-Step 'a missing item is an error, not an empty answer' {
    $result = (Invoke-Rpc 'tools/call' @{ name = 'get_task'; arguments = @{ id = [guid]::NewGuid().ToString() } }).result

    if (-not $result.isError) {
        throw 'A non-existent id returned success - an agent would report "nothing found" as fact.'
    }
    Write-Host "  $($result.content[0].text)"
}

# --- Write round-trip -----------------------------------------------------------
# Deliberately a report link: it is the cheapest thing in the gallery to create, nothing else can
# reference it, and its delete is confirm-gated — so one throwaway entry exercises create, partial
# update, the refuse-without-confirm gate and delete without leaving anything behind. The step
# order matters: if create succeeds and a later step fails, the entry is still cleaned up, because
# an ordinary step failure is recorded and the run continues. The one exception is a session that
# expires mid-round-trip, which aborts the run — Test-Step prints the orphaned entry's URL so it can
# be removed by hand.
if ($ExpectWrites) {
    $createdReportId = $null
    $createdVersion = $null
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

    Test-Step 'create_report' {
        $result = (Invoke-Rpc 'tools/call' @{
                name      = 'create_report'
                arguments = @{
                    name        = "MCP smoke test $stamp"
                    description = 'Created by scripts/test-mcp-server.ps1. Safe to delete.'
                    url         = 'https://example.com/mcp-smoke-test'
                    kind        = 'Report'
                }
            }).result
        if ($result.isError) { throw $result.content[0].text }

        $report = $result.content[0].text | ConvertFrom-Json
        $script:createdReportId = $report.id
        $script:createdVersion = $report.version
        Write-Host "  created $($report.id)"
        Write-Host "  link:   $($report.galleryUrl)"

        if ($report.createdByDisplayName -eq 'Anonymous contributor') {
            throw 'The entry was attributed to the anonymous fallback user - FR-004 requires a named principal.'
        }
        Write-Host "  author: $($report.createdByDisplayName)"
    }

    Test-Step 'update_report changes only what was asked' {
        if (-not $createdReportId) { return 'skipped' }

        $result = (Invoke-Rpc 'tools/call' @{
                name      = 'update_report'
                arguments = @{
                    id              = $createdReportId
                    description     = 'Updated by the smoke test. Safe to delete.'
                    expectedVersion = $createdVersion
                }
            }).result
        if ($result.isError) { throw $result.content[0].text }

        $report = $result.content[0].text | ConvertFrom-Json
        if ($report.name -ne "MCP smoke test $stamp") {
            throw "The name was not preserved through a partial update - got '$($report.name)'."
        }
        if ($report.url -ne 'https://example.com/mcp-smoke-test') {
            throw "The url was not preserved through a partial update - got '$($report.url)'."
        }
        Write-Host '  description changed; name, url and kind preserved'
    }

    Test-Step 'a stale expectedVersion is refused as a conflict' {
        if (-not $createdReportId) { return 'skipped' }

        # $createdVersion is now stale: the update above replaced it.
        $result = (Invoke-Rpc 'tools/call' @{
                name      = 'update_report'
                arguments = @{ id = $createdReportId; name = 'Should not stick'; expectedVersion = $createdVersion }
            }).result

        if (-not $result.isError) {
            throw 'A stale version was accepted - a concurrent edit would be lost silently.'
        }
        $firstLine = @($result.content[0].text -split "`r?`n")[0]
        Write-Host "  $firstLine"
    }

    Test-Step 'delete_report is refused without confirm' {
        if (-not $createdReportId) { return 'skipped' }

        $result = (Invoke-Rpc 'tools/call' @{ name = 'delete_report'; arguments = @{ id = $createdReportId } }).result
        if (-not $result.isError) {
            throw 'The entry was deleted without confirmation - the confirm gate is not working.'
        }
        $firstLine = @($result.content[0].text -split "`r?`n")[0]
        Write-Host "  $firstLine"
    }

    Test-Step 'delete_report with confirm cleans up' {
        if (-not $createdReportId) { return 'skipped' }

        $result = (Invoke-Rpc 'tools/call' @{
                name = 'delete_report'; arguments = @{ id = $createdReportId; confirm = $true }
            }).result
        if ($result.isError) { throw $result.content[0].text }

        $check = (Invoke-Rpc 'tools/call' @{ name = 'get_report'; arguments = @{ id = $createdReportId } }).result
        if (-not $check.isError) { throw 'The entry is still readable after a confirmed delete.' }
        Write-Host '  deleted and no longer readable'
    }
}

Write-Host ''
if ($failures -gt 0) {
    Write-Host "$failures step(s) failed." -ForegroundColor Red
    exit 1
}

Write-Host "All steps passed for collection $CollectionId (anonymous: $Anonymous). This proves only this smoke run, not native client compatibility." -ForegroundColor Green
