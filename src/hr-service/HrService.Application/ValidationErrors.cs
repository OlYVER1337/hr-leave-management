namespace HrService.Application.Common;
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new();

    public bool HasErrors => _errors.Count > 0;
    public void Add(string field, string message)  
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = new List<string>();
            _errors[field] = list;
        }
        list.Add(message);
    }

    public void ThrowAnyThing()
    {
        if (HasErrors)
        {
            throw new RequestValidationException(_errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
        }
    }
}

public static class Problems
{
    public static RequestValidationException Invalid(string field, string message)=>
   new(new Dictionary <string, string[]>  { [field] = [message]    });

    }
