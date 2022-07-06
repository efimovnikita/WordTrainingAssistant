using System;
using System.Collections.Generic;

namespace WordTrainingAssistant.Shared.Models;

[Serializable] public class SetRoot
{
    public List<SetDatum> data { get; set; }
}