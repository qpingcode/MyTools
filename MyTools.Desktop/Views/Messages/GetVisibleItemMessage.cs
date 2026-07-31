using CommunityToolkit.Mvvm.Messaging.Messages;
using MyTools.Common;

namespace MyTools.Desktop.Components;

public class GetVisibleItemMessage : RequestMessage<List<ResultItem>>;