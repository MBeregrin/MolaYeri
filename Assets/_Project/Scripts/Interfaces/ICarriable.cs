using UnityEngine;
using Unity.Netcode;

namespace WeBussedUp.Interfaces
{
    public interface ICarriable
    {
        bool            IsCarried    { get; }
        Transform       transform    { get; }
        NetworkObject   NetworkObject { get; }

        void RequestDropServerRpc(Vector3 dropPosition);
    }
}