<#
.SYNOPSIS
    Removes the LiveStreamSound firewall rules from the local machine.

.DESCRIPTION
    Counterpart to Add-LiveStreamSoundFirewallRules.ps1. Removes all rules in
    the "LiveStreamSound" rule group. Safe to run even if no rules exist.

.EXAMPLE
    powershell.exe -ExecutionPolicy Bypass -File .\Remove-LiveStreamSoundFirewallRules.ps1

.NOTES
    MSI uninstall already removes these rules under normal circumstances. Use
    this script only if the MSI got removed incorrectly or if you pushed the
    rules via GPO without installing the app and now want to pull them back.
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'SilentlyContinue'

$ruleNames = @(
    'LiveStreamSound (TCP Control)',
    'LiveStreamSound (UDP Audio)',
    'LiveStreamSound (TCP Invite)'
)

$removed = 0
foreach ($name in $ruleNames) {
    $rule = Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue
    if ($rule) {
        Remove-NetFirewallRule -DisplayName $name
        Write-Host "Removed: $name"
        $removed++
    }
}

# Also catch anything left over in the group (defensive — e.g. a renamed rule
# that still has the LiveStreamSound group tag).
$groupLeftovers = Get-NetFirewallRule -Group 'LiveStreamSound' -ErrorAction SilentlyContinue
foreach ($rule in $groupLeftovers) {
    Remove-NetFirewallRule -Name $rule.Name
    Write-Host "Removed group leftover: $($rule.DisplayName)"
    $removed++
}

Write-Host "Done. $removed rule(s) removed."
