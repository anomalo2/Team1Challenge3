using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Development probe. Watches live traffic during Play and writes a report, so
/// that "do the cars actually drive?" can be answered automatically instead of
/// by someone staring at the Game view.
///
/// It is added to the scene by TrafficSetupHarness before it enters play mode,
/// and is not saved into the scene. Harmless if left on an object: it only
/// measures and never changes anything.
/// </summary>
public class TrafficPlayProbe : MonoBehaviour
{
    [Header("Run")]
    [Tooltip("Seconds of play to observe before writing the report.")]
    public float duration = 12f;
    [Tooltip("Report file, relative to the project root.")]
    public string outputPath = "TrafficPlayTest.log";

    [Header("Thresholds")]
    [Tooltip("A car that never exceeds this speed counts as stuck.")]
    public float movingSpeedThreshold = 0.5f;
    [Tooltip("A car must cover at least this much ground to count as driving.")]
    public float movingDistanceThreshold = 2f;

    public bool Finished { get; private set; }

    class Record
    {
        public string Name;
        public Vector3 FirstPos;
        public Vector3 LastPos;
        public float Distance;
        public float MaxSpeed;
        public float FirstSeen;
        public float LastSeen;
        public bool Gone;
    }

    readonly Dictionary<int, Record> records = new Dictionary<int, Record>();

    float elapsed;
    int peakAlive;
    int sampleCount;
    float aliveSum;

    void Update()
    {
        if (Finished) return;

        elapsed += Time.deltaTime;

        CarController[] cars = Object.FindObjectsByType<CarController>(FindObjectsSortMode.None);

        peakAlive = Mathf.Max(peakAlive, cars.Length);
        aliveSum += cars.Length;
        sampleCount++;

        HashSet<int> seenNow = new HashSet<int>();

        foreach (CarController car in cars)
        {
            int id = car.GetInstanceID();
            seenNow.Add(id);

            Record rec;
            if (!records.TryGetValue(id, out rec))
            {
                rec = new Record
                {
                    Name = car.gameObject.name,
                    FirstPos = car.transform.position,
                    LastPos = car.transform.position,
                    FirstSeen = elapsed,
                };
                records[id] = rec;
            }

            float step = Vector3.Distance(rec.LastPos, car.transform.position);
            rec.Distance += step;
            rec.LastPos = car.transform.position;
            rec.LastSeen = elapsed;
            rec.MaxSpeed = Mathf.Max(rec.MaxSpeed, car.CurrentSpeed);
        }

        foreach (KeyValuePair<int, Record> kv in records)
            if (!seenNow.Contains(kv.Key)) kv.Value.Gone = true;

        if (elapsed >= duration) WriteReport();
    }

    void WriteReport()
    {
        Finished = true;

        int driving = 0;
        int stuck = 0;
        int despawned = 0;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== TrafficPlayProbe ===");
        sb.AppendLine(string.Format("observed {0:0.#}s, {1} frames", elapsed, sampleCount));
        sb.AppendLine(string.Format("cars seen: {0}, peak alive: {1}, mean alive: {2:0.##}",
            records.Count, peakAlive, sampleCount > 0 ? aliveSum / sampleCount : 0f));
        sb.AppendLine();

        foreach (KeyValuePair<int, Record> kv in records)
        {
            Record r = kv.Value;

            bool moved = r.Distance >= movingDistanceThreshold
                         && r.MaxSpeed >= movingSpeedThreshold;

            if (moved) driving++;
            else stuck++;

            if (r.Gone) despawned++;

            sb.AppendLine(string.Format(
                "  {0,-28} dist={1,6:0.#} maxSpeed={2,5:0.##} alive={3,5:0.#}s {4}{5}",
                r.Name, r.Distance, r.MaxSpeed, r.LastSeen - r.FirstSeen,
                moved ? "DRIVING" : "STUCK",
                r.Gone ? " (despawned)" : ""));
        }

        sb.AppendLine();
        sb.AppendLine(string.Format("driving={0} stuck={1} despawned={2}", driving, stuck, despawned));

        bool pass = records.Count > 0 && driving > 0 && stuck == 0;
        sb.AppendLine(pass
            ? "RESULT: PASS - traffic spawned and every car drove"
            : "RESULT: FAIL - " + (records.Count == 0
                ? "no cars ever spawned"
                : stuck + " car(s) never moved"));

        string text = sb.ToString();
        Debug.Log(text);

#if UNITY_EDITOR
        try { System.IO.File.WriteAllText(outputPath, text); }
        catch (System.Exception e) { Debug.LogError("[TrafficPlayProbe] write failed: " + e.Message); }
#endif
    }
}
