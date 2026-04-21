<#
.SYNOPSIS
    Creates the LiveStreamSound firewall rules on the local machine.

.DESCRIPTION
    Adds three inbound allow-rules scoped to LocalSubnet so LiveStreamSound
    Host and Client instances can talk on the school LAN without Windows
    Defender Firewall blocking anything. Safe to re-run (idempotent: removes
    existing rules with the same names first).

    Intended use:
      - GPO Startup Script (Computer Config → Policies → Windows Settings →
        Scripts → Startup) on the Matura room PCs
      - Intune Proactive Remediation Detection+Remediation script
      - One-shot Admin Powershell for troubleshooting

    Requires: Administrator rights. Windows 10 / 11 (NetFirewall module).

.EXAMPLE
    # Run once as Administrator:
    powershell.exe -ExecutionPolicy Bypass -File .\Add-LiveStreamSoundFirewallRules.ps1

.NOTES
    The same rules are added automatically by the LiveStreamSound MSI installer
    under HKLM\Software\LiveStreamSound\firewall. Use this script only if you
    want the rules to exist BEFORE the app is installed, or on machines that
    won't get the MSI but need to communicate with the session host (rare).
#>

[CmdletBinding()]
param(
    [string]$Profile = 'Any',  # Domain,Private,Public,Any
    [string]$RemoteAddress = 'LocalSubnet'
)

$ErrorActionPreference = 'Stop'

$rules = @(
    [pscustomobject]@{ Name = 'LiveStreamSound (TCP Control)'; Port = 5000; Protocol = 'TCP'; Description = 'Host control channel (session codes, per-client commands).' },
    [pscustomobject]@{ Name = 'LiveStreamSound (UDP Audio)';   Port = 5001; Protocol = 'UDP'; Description = 'Host audio stream fan-out to clients.' },
    [pscustomobject]@{ Name = 'LiveStreamSound (TCP Invite)';  Port = 5002; Protocol = 'TCP'; Description = 'Idle client listens for host-initiated session invitations.' }
)

Write-Host "Applying LiveStreamSound firewall rules (Profile=$Profile, RemoteAddress=$RemoteAddress)..."
foreach ($rule in $rules) {
    # Remove any existing rule with the same display name so we don't end up
    # with duplicates on re-runs.
    $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "  - Removing existing rule: $($rule.Name)"
        Remove-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
    }

    Write-Host "  + Adding: $($rule.Name) — $($rule.Protocol)/$($rule.Port)"
    New-NetFirewallRule `
        -DisplayName $rule.Name `
        -Description $rule.Description `
        -Direction Inbound `
        -Protocol $rule.Protocol `
        -LocalPort $rule.Port `
        -Action Allow `
        -Profile $Profile `
        -RemoteAddress $RemoteAddress `
        -Group 'LiveStreamSound' `
        -Enabled True | Out-Null
}

Write-Host "Done. $($rules.Count) LiveStreamSound firewall rules installed."
