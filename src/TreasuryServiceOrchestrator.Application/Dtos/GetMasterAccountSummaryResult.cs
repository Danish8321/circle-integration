using TreasuryServiceOrchestrator.Domain;

namespace TreasuryServiceOrchestrator.Application.Dtos;

public sealed record GetMasterAccountSummaryResult(
    Money MainWalletBalance, Money TotalSubAccountBalance, int SubAccountCount);
