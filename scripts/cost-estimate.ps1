# Offline estimate; does not call any service or read user data.
# Prices verified 2026-10-09: https://api-docs.deepseek.com/zh-cn/quick_start/pricing/
$formeInputPrice = 2.0 # RMB per million input tokens, peak / cache miss
$formeOutputPrice = 8.0 # RMB per million output tokens, peak
$formeTypicalInput = 0
for ($formeTurn = 0; $formeTurn -lt 10; $formeTurn++) {
    # Assumptions: 600-token role/preferences; 200-token user message;
    # 120-token assistant answer, with framing allowance of 64 per prior exchange.
    $formeTypicalInput += [Math]::Min(4096, 600 + 200 + $formeTurn * (200 + 120 + 64))
}
$formeConversations = 3 * 30
$formeTypicalInput *= $formeConversations
$formeTypicalOutput = 120 * 10 * $formeConversations
$formeUpperInput = 4096 * 10 * $formeConversations
$formeUpperOutput = 512 * 10 * $formeConversations
# Three explicit connection tests, and 5% manual retries (not automatic).
$formeTestCost = 3 * (64 * $formeInputPrice + 100 * $formeOutputPrice) / 1000000
$formeTypicalCost = ($formeTypicalInput * $formeInputPrice + $formeTypicalOutput * $formeOutputPrice) / 1000000 * 1.05 + $formeTestCost
$formeUpperCost = ($formeUpperInput * $formeInputPrice + $formeUpperOutput * $formeOutputPrice) / 1000000 * 1.05 + $formeTestCost
[pscustomobject]@{
    VerifiedDate = '2026-10-09'
    Model = 'deepseek-flash (non-thinking)'
    MonthlyBaseRequests = 900
    ManualRetryAllowance = '5%'
    TypicalEstimatedRMB = [Math]::Round($formeTypicalCost, 2)
    BudgetBasedUpperEstimatedRMB = [Math]::Round($formeUpperCost, 2)
    Caveat = 'Assumptions, not a billing guarantee; prices can change. Local functions cost no API calls.'
} | Format-List
