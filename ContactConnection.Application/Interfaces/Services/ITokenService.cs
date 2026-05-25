using ContactConnection.Domain.Entities;

namespace ContactConnection.Application.Interfaces.Services;

public interface ITokenService
{
    string GenerateToken(Agent agent, Tenant tenant);
    string GeneratePreAuthToken(Agent agent, Tenant tenant);
}
