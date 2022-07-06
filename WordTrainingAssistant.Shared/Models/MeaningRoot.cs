using System;

namespace WordTrainingAssistant.Shared.Models;

[Serializable] public class MeaningRoot
{
    public string text { get; set; }

    public Translation translation { get; set; }
}