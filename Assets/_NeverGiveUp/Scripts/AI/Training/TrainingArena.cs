using System;
using UnityEngine;
using Unity.MLAgents;

namespace NeverGiveUp.Training
{
    // This component exists only in AITraining. Game scenes never reset characters.
    public sealed class TrainingArena : MonoBehaviour
    {
        public int index;
        public int initialLevel = 1;
        public int seed = 731;
        public Vector3 Goal { get; private set; }
        public int Level { get; private set; }
        public int EpisodeSeed { get; private set; }
        readonly GameObject[] surfaces = new GameObject[5];
        GameObject marker;
        int episode;
        float moveAt;
        Vector3 boxStart;
        System.Random random;
        float Range(float a, float b) => a + (b-a)*(float)random.NextDouble();
        public Vector3 ResetLayout()
        {
            Level = Mathf.Clamp(Mathf.RoundToInt(Academy.Instance.EnvironmentParameters.GetWithDefault("level", initialLevel)),1,5);
            int baseSeed = Mathf.RoundToInt(Academy.Instance.EnvironmentParameters.GetWithDefault("seed",seed));
            EpisodeSeed = baseSeed + index * 100003 + episode++;
            random = new System.Random(EpisodeSeed);
            for(int i=0;i<surfaces.Length;i++)
            {
                if(!surfaces[i])
                {
                    surfaces[i]=GameObject.CreatePrimitive(PrimitiveType.Cube);
                    surfaces[i].name="Training surface " + i;
                    surfaces[i].transform.SetParent(transform);
                }
                surfaces[i].SetActive(false);
            }
            // Mirror direction each episode; no absolute position is observed by the policy.
            float direction = random.Next(2)==0 ? -1:1;
            float start = -direction*Range(3f,4.5f);
            Surface(0,0,-.3f,22,.6f);
            float targetX=direction*Range(3f,4.5f), targetY=0;
            if(Level>=2)
            {
                float x=direction*Range(-.5f,.7f);
                float h=Level==2 ? Range(.35f,.85f):Range(.8f,1.65f);
                float width=Level==2 ? Range(.65f,1.25f):Range(2f,3f);
                Surface(1,x,h/2,width,h);
                if(Level==3) {targetX=x;targetY=h;}
                if(Level>=4)
                {
                    float h2=h+Range(.6f,1.25f);
                    float x2=x+direction*Range(3f,4f);
                    Surface(2,x2,h2/2,Range(2.0f,2.7f),h2);
                    targetX=x2;targetY=h2;
                }
            }
            Goal=transform.position+new Vector3(targetX,targetY,0);
            if(!marker)
            {
                marker=GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(marker.GetComponent<Collider>());
                marker.transform.SetParent(transform);
                marker.transform.localScale=Vector3.one*.25f;
            }
            marker.transform.position=Goal+Vector3.up*.3f;
            moveAt=Time.time+Range(1.2f,2.3f);
            boxStart=surfaces[1].transform.position;
            Physics.SyncTransforms();
            return transform.position+new Vector3(start,1.02f,0);
        }
        void Surface(int i,float x,float y,float width,float height)
        {
            var t=surfaces[i].transform;
            t.localPosition=new Vector3(x,y,0);t.localScale=new Vector3(width,height,3);
            surfaces[i].SetActive(true);
        }
        public void Tick()
        {
            if(Level==5 && Time.time>=moveAt)
            {
                // A bounded environmental change, independent of policy output.
                surfaces[1].transform.position=boxStart+Vector3.right*.65f;
                moveAt=float.PositiveInfinity;
            }
        }
    }
}
