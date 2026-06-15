/// <summary>
/// Result of resolving dependency scope transfer.
/// </summary>
public class DependencyScopeTransferResult {

  private bool _Success;
  private string[] _TargetScopes;
  private string _ErrorMessage;

  private DependencyScopeTransferResult(
    bool success,
    string[] targetScopes,
    string errorMessage
  ) {
    _Success = success;
    _TargetScopes = targetScopes;
    _ErrorMessage = errorMessage;
  }

  public bool IsSuccess {
    get {
      return _Success;
    }
  }

  public string[] TargetScopes {
    get {
      return _TargetScopes;
    }
  }

  public string ErrorMessage {
    get {
      return _ErrorMessage;
    }
  }

  public static DependencyScopeTransferResult Success(string[] targetScopes) {
    return new DependencyScopeTransferResult(true, targetScopes, null);
  }

  public static DependencyScopeTransferResult Fail(string errorMessage) {
    return new DependencyScopeTransferResult(false, new string[0], errorMessage);
  }

}