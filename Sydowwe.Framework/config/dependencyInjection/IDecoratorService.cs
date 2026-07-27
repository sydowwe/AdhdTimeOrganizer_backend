namespace Sydowwe.Framework.config.dependencyInjection;

// Marks a service as a decorator of a Core service.
// Any class implementing this interface will be registered via services.Decorate()
// rather than being added as a new registration — the Core implementation is
// preserved and wrapped, not silently replaced.
//
// Usage: implement both the decorated interface and IDecoratorService:
//   public class HbWorkHourCalculatorService : IWorkHourCalculatorService, IScopedService, IDecoratorService { ... }
//
// The decorated interface is resolved as the one interface the decorator both implements and
// accepts as a constructor parameter (the injected "inner") — independent of parameter order.
public interface IDecoratorService;