namespace FleetCore;

public interface IWmsOrderRepository
{
    Task SaveAsync(WmsOrder order, CancellationToken cancellationToken);
    Task SaveAllAsync(IEnumerable<WmsOrder> orders, CancellationToken cancellationToken);
    Task<IEnumerable<WmsOrder>> LoadAllAsync(CancellationToken cancellationToken);
    Task<WmsOrder?> GetAsync(string orderId, CancellationToken cancellationToken);
}