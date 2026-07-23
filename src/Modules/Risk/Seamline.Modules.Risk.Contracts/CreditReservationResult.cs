namespace Seamline.Modules.Risk.Contracts;

public enum CreditReservationOutcome
{
    Reserved = 1,
    Breached = 2
}

public sealed record CreditReservationResult(CreditReservationOutcome Outcome, decimal ExistingExposure, decimal CreditLimit);
