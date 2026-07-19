namespace TreasuryServiceOrchestrator.Domain;

public class SubAccount
{
    public Guid Id { get; private set; }
    public string ClientCompanyId { get; private set; } = string.Empty;
    public SubAccountLifecycleState LifecycleState { get; private set; }
    public bool IsDisabled { get; private set; }
    public string? CircleWalletId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private SubAccount()
    {
    }

    public static SubAccount Create(string clientCompanyId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(clientCompanyId))
        {
            throw new ArgumentException("ClientCompanyId is required.", nameof(clientCompanyId));
        }

        return new SubAccount
        {
            Id = Guid.NewGuid(),
            ClientCompanyId = clientCompanyId,
            LifecycleState = SubAccountLifecycleState.Created,
            IsDisabled = false,
            CreatedAtUtc = nowUtc,
        };
    }

    public void BeginCompliance(string circleWalletId)
    {
        if (LifecycleState != SubAccountLifecycleState.Created)
        {
            throw new InvalidOperationException(
                $"Cannot begin compliance from state {LifecycleState}; expected {SubAccountLifecycleState.Created}.");
        }

        if (string.IsNullOrWhiteSpace(circleWalletId))
        {
            throw new ArgumentException("CircleWalletId is required.", nameof(circleWalletId));
        }

        CircleWalletId = circleWalletId;
        LifecycleState = SubAccountLifecycleState.PendingCompliance;
    }

    public void MarkRejected()
    {
        if (LifecycleState != SubAccountLifecycleState.PendingCompliance)
        {
            throw new InvalidOperationException(
                $"Cannot mark rejected from state {LifecycleState}; expected {SubAccountLifecycleState.PendingCompliance}.");
        }

        LifecycleState = SubAccountLifecycleState.Rejected;
    }

    public void MarkAccepted()
    {
        if (LifecycleState != SubAccountLifecycleState.PendingCompliance)
        {
            throw new InvalidOperationException(
                $"Cannot mark accepted from state {LifecycleState}; expected {SubAccountLifecycleState.PendingCompliance}.");
        }

        LifecycleState = SubAccountLifecycleState.Active;
    }

    public void SetDisabled(bool disabled)
    {
        IsDisabled = disabled;
    }

    public void ResubmitCompliance()
    {
        if (LifecycleState != SubAccountLifecycleState.Rejected)
        {
            throw new InvalidOperationException(
                $"Cannot resubmit compliance from state {LifecycleState}; expected {SubAccountLifecycleState.Rejected}.");
        }

        LifecycleState = SubAccountLifecycleState.PendingCompliance;
    }

    // Resubmission issues a fresh externalEntities record at the provider, with its own wallet
    // id distinct from the original rejected attempt's — the sub-account must track the id the
    // provider will key its decision webhook off of, or that webhook can never find this row
    // again (CircleWalletId is the lookup key in ProcessExternalEntityDecisionHandler).
    public void UpdateCircleWalletId(string circleWalletId)
    {
        if (string.IsNullOrWhiteSpace(circleWalletId))
        {
            throw new ArgumentException("CircleWalletId is required.", nameof(circleWalletId));
        }

        CircleWalletId = circleWalletId;
    }
}
