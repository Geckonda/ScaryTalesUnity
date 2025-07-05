using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Assets.Scripts.Utilities
{
    public static class CoroutineUtils
    {
        public static Task WaitForCoroutine(MonoBehaviour owner, IEnumerator coroutine)
        {
            var tcs = new TaskCompletionSource<bool>();
            owner.StartCoroutine(Run());

            IEnumerator Run()
            {
                yield return coroutine;
                tcs.SetResult(true);
            }

            return tcs.Task;
        }
    }

}
