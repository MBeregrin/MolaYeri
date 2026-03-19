using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using WeBussedUp.NPC;

namespace WeBussedUp.NPC
{
    /// <summary>
    /// Araçtan NPC çıkarır. VehicleAI.SpawnPassengersServerRpc tarafından tetiklenir.
    /// Cinsiyet oranını ayarlar, CustomerAI'ya ihtiyaç listesi atar.
    /// </summary>
    public class CustomerSpawner : MonoBehaviour
    {
        // ─── Inspector ───────────────────────────────────────────
        [Header("NPC Prefabları")]
        [SerializeField] private GameObject _malePrefab;
        [SerializeField] private GameObject _femalePrefab;
        [SerializeField] private GameObject _childPrefab;

        [Header("Cinsiyet Oranları")]
        [Range(0f, 1f)] [SerializeField] private float _maleRatio   = 0.45f;
        [Range(0f, 1f)] [SerializeField] private float _femaleRatio = 0.45f;
        // Çocuk oranı = 1 - male - female

        [Header("İhtiyaç Dağılımı")]
        [SerializeField] private float _shoppingChance  = 0.7f;
        [SerializeField] private float _cafeChance      = 0.4f;
        [SerializeField] private float _restroomChance  = 0.3f;
        [SerializeField] private float _fuelChance      = 0.2f;
        [SerializeField] private float _carWashChance   = 0.15f;
        

        // ─── Public API ──────────────────────────────────────────
        /// <summary>
        /// VehicleAI tarafından çağrılır.
        /// </summary>
        public void SpawnPassengers(VehicleAI vehicle, int count)
        {
            for (int i = 0; i < count; i++)
            {
                CustomerGender gender  = DetermineGender();
                GameObject     prefab  = GetPrefab(gender);

                if (prefab == null) continue;

                // Araçtan biraz offset ile doğur
                Vector3 spawnPos = vehicle.ExitPoint != null
                    ? vehicle.ExitPoint.position + Random.insideUnitSphere * 0.5f
                    : vehicle.transform.position  + Vector3.right * i;

                spawnPos.y = vehicle.transform.position.y;

                GameObject npcObj = Instantiate(prefab, spawnPos, Quaternion.identity);

                if (!npcObj.TryGetComponent(out NetworkObject netObj))
                {
                    Destroy(npcObj);
                    continue;
                }

                netObj.Spawn();

                if (npcObj.TryGetComponent(out CustomerAI ai))
                {
                    List<CustomerNeed> needs = GenerateNeedList(gender);
                    ai.Initialize(gender, needs, vehicle.ExitPoint, vehicle.Type);
                }
            }
        }

        // ─── İhtiyaç Üretimi ─────────────────────────────────────
        private List<CustomerNeed> GenerateNeedList(CustomerGender gender)
        {
            var needs = new List<CustomerNeed>();

            // Çocuklar sadece market ve kafe gider
            if (gender == CustomerGender.Child)
            {
                if (Random.value < _shoppingChance) needs.Add(CustomerNeed.Shopping);
                if (Random.value < _cafeChance)     needs.Add(CustomerNeed.Cafe);
                return needs;
            }

            if (Random.value < _shoppingChance)  needs.Add(CustomerNeed.Shopping);
            if (Random.value < _cafeChance)      needs.Add(CustomerNeed.Cafe);
            if (Random.value < _restroomChance)  needs.Add(CustomerNeed.Restroom);
            if (Random.value < _fuelChance)      needs.Add(CustomerNeed.Fuel);
            if (Random.value < _carWashChance)   needs.Add(CustomerNeed.CarWash);

            // En az bir ihtiyaç olsun
            if (needs.Count == 0)
                needs.Add(CustomerNeed.Shopping);

            // Rastgele sırala
            needs.Sort((a, b) => Random.value > 0.5f ? 1 : -1);

            return needs;
        }

        // ─── Cinsiyet ────────────────────────────────────────────
        private CustomerGender DetermineGender()
        {
            float roll = Random.value;

            if (roll < _maleRatio)                      return CustomerGender.Male;
            if (roll < _maleRatio + _femaleRatio)       return CustomerGender.Female;
            return CustomerGender.Child;
        }

        private GameObject GetPrefab(CustomerGender gender) => gender switch
        {
            CustomerGender.Male   => _malePrefab,
            CustomerGender.Female => _femalePrefab,
            CustomerGender.Child  => _childPrefab,
            _                     => _malePrefab
        };
    }
}