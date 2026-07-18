using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Admin;

public sealed record GetMasterAccountSummaryResult(
    Money MainWalletBalance, Money TotalSubAccountBalance, int SubAccountCount);
