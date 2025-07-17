using ScaryTales.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ScaryTales.Interaction_Entities.EnvUnity
{
    public class UnityNotifier : INotifier
    {
        public string Username { get; private set; }
        public UnityNotifier(string username)
        {
            this.Username = username;
        }
        public void Notify(string message)
        {
            Debug.Log($"|{Username}| {message}");
        }
    }
}
