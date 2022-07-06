using System;
using System.Collections.Generic;

namespace WordTrainingAssistant.Shared.Models;

[Serializable] public class SetsRoot
{
    public List<SetsDatum> data { get; set; }
}