using MakarovPhysics.Renderer;

namespace Promzona.Game;

/// <summary>Game-local GPU skinning. One rest mesh, four influences and a bounded
/// texture of joint palettes compiled from the editable source GLB skeleton.</summary>
internal sealed unsafe class CitySkin : IDisposable
{
    private uint _vao, _vertices, _indices, _bones, _instances;
    private int _instanceCapacity;
    private readonly int _count;
    private readonly Vec3[] _headCaps;
    public int VertexCount { get; }
    public int JointCount { get; }
    public int PoseCount { get; }
    public long GpuBytes { get; }
    public CitySkin(string path)
    {
        using var reader=new BinaryReader(File.OpenRead(path));
        if(reader.ReadUInt32()!=0x314B5343)throw new InvalidDataException("Invalid city skin header");
        VertexCount=reader.ReadInt32();_count=reader.ReadInt32();JointCount=reader.ReadInt32();PoseCount=reader.ReadInt32();
        if(VertexCount is < 1 or > 1000000||_count is < 3 or > 3000000||_count%3!=0||JointCount is < 1 or > 128||PoseCount is not (193 or 256 or 320 or 384 or 448 or 576))
            throw new InvalidDataException("Unsupported city skin dimensions");
        GpuBytes=(long)VertexCount*64+_count*4L+JointCount*PoseCount*64L;
        if(reader.BaseStream.Length!=20+GpuBytes)throw new InvalidDataException("Truncated city skin");
        var vertices=new float[VertexCount*16];var indices=new uint[_count];var bones=new float[JointCount*PoseCount*16];
        for(int i=0;i<vertices.Length;i++){vertices[i]=reader.ReadSingle();if(!float.IsFinite(vertices[i]))throw new InvalidDataException("Non-finite skin vertex");}
        for(int i=0;i<indices.Length;i++){indices[i]=reader.ReadUInt32();if(indices[i]>=VertexCount)throw new InvalidDataException("Skin index out of bounds");}
        for(int i=0;i<bones.Length;i++){bones[i]=reader.ReadSingle();if(!float.IsFinite(bones[i]))throw new InvalidDataException("Non-finite joint matrix");}
        for(int i=0;i<VertexCount;i++){
            float sum=0;for(int j=0;j<4;j++){
                float joint=vertices[i*16+8+j],weight=vertices[i*16+12+j];
                if(joint<0||joint>=JointCount||joint!=MathF.Floor(joint)||weight<0)throw new InvalidDataException("Invalid skin influences");sum+=weight;
            }if(Math.Abs(sum-1)>.001)throw new InvalidDataException("Unnormalized skin weights");
        }
        // A tiny CPU socket table follows the exact GPU deformation. Keeping just
        // one crown point per baked pose avoids skinning the character on the CPU.
        Vec3 SkinPoint(int v,int pose){
            int k=v*16;float x=vertices[k],y=vertices[k+1],z=vertices[k+2];Vec3 point=default;
            for(int j=0;j<4;j++){float weight=vertices[k+12+j];if(weight<=0)continue;
                int b=(pose*JointCount+(int)vertices[k+8+j])*16;
                point+=new Vec3(bones[b]*x+bones[b+4]*y+bones[b+8]*z+bones[b+12],
                    bones[b+1]*x+bones[b+5]*y+bones[b+9]*z+bones[b+13],
                    bones[b+2]*x+bones[b+6]*y+bones[b+10]*z+bones[b+14])*weight;
            }return point;
        }
        // Source rest vertices can use centimetres or arbitrary bind space.
        // Select the crown in the first standing pose, already in model metres.
        var standing=Enumerable.Range(0,VertexCount).Select(i=>SkinPoint(i,0)).ToArray();
        float crown=standing.Max(standingPoint=>standingPoint.Y);
        var top=Enumerable.Range(0,VertexCount).Where(i=>standing[i].Y>=crown-.025f).ToArray();
        _headCaps=new Vec3[PoseCount];
        for(int pose=0;pose<PoseCount;pose++){
            Vec3 sum=default;
            foreach(int v in top)sum+=SkinPoint(v,pose);
            _headCaps[pose]=sum*(1f/top.Length);
        }
        try {
            _vao=GL.GenVertexArray();_vertices=GL.GenBuffer();_indices=GL.GenBuffer();GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER,_vertices);GL.BufferData(GL.ARRAY_BUFFER,vertices,GL.STATIC_DRAW);
            int[] sizes=[3,3,2,4,4];int[] offsets=[0,12,24,32,48];
            for(uint i=0;i<5;i++){GL.VertexAttribPointer(i,sizes[i],GL.FLOAT,false,64,offsets[i]);GL.EnableVertexAttribArray(i);}
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER,_indices);GL.BufferData(GL.ELEMENT_ARRAY_BUFFER,indices,GL.STATIC_DRAW);GL.BindVertexArray(0);
            _bones=GL.GenTexture();GL.BindTexture(GL.TEXTURE_2D,_bones);
            fixed(float* bonePointer=bones)GL.TexImage2D(GL.TEXTURE_2D,0,0x8814,JointCount*4,PoseCount,0,GL.RGBA,GL.FLOAT,bonePointer);
            GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MIN_FILTER,(int)GL.NEAREST);GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_MAG_FILTER,(int)GL.NEAREST);
            GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_WRAP_S,(int)GL.CLAMP_TO_EDGE);GL.TexParameteri(GL.TEXTURE_2D,GL.TEXTURE_WRAP_T,(int)GL.CLAMP_TO_EDGE);
        }catch {Dispose();throw;}
    }
    internal Vec3 Crown(float from,float to,float blend)
    {
        Vec3 At(float frame){int poseIndex=Math.Clamp((int)frame,0,PoseCount-1),start=poseIndex/64*64,next=Math.Min(PoseCount-1,start+(poseIndex+1-start)%64);
            return Vec3.Lerp(_headCaps[poseIndex],_headCaps[next],frame-MathF.Floor(frame));}
        return Vec3.Lerp(At(from),At(to),Math.Clamp(blend,0,1));
    }
    // Called after the actor's matching program has been bound; no program switch here.
    public void Draw(Shader shader,float pose,float from=-1,float blend=1)
    {
        shader.SetInt("uSkin",1);shader.SetInt("uBones",11);shader.SetFloat("uPose",pose);shader.SetFloat("uPreviousPose",from<0?pose:from);shader.SetFloat("uPoseBlend",blend);
        GL.ActiveTexture(GL.TEXTURE0+11);GL.BindTexture(GL.TEXTURE_2D,_bones);GL.ActiveTexture(GL.TEXTURE0);
        GL.BindVertexArray(_vao);GL.DrawElements(GL.TRIANGLES,_count,GL.UNSIGNED_INT,0);GL.BindVertexArray(0);
    }
    // Caller has bound the matching instanced program. 24 floats per instance:
    // column-major model matrix, two animation poses/blend, and RGB tint.
    public void DrawInstances(Shader shader,float[] data,int count)
    {
        if(count==0)return;
        if(count<0||count>data.Length/24)throw new ArgumentOutOfRangeException(nameof(count));
        shader.SetInt("uSkin",1);shader.SetInt("uBones",11);
        GL.ActiveTexture(GL.TEXTURE0+11);GL.BindTexture(GL.TEXTURE_2D,_bones);GL.ActiveTexture(GL.TEXTURE0);
        GL.BindVertexArray(_vao);
        if(_instances==0){
            _instances=GL.GenBuffer();GL.BindBuffer(GL.ARRAY_BUFFER,_instances);
            for(uint i=0;i<6;i++){
                GL.VertexAttribPointer(5+i,4,GL.FLOAT,false,96,(int)i*16);
                GL.EnableVertexAttribArray(5+i);GL.VertexAttribDivisor(5+i,1);
            }
        }else GL.BindBuffer(GL.ARRAY_BUFFER,_instances);
        if(count>_instanceCapacity){_instanceCapacity=Math.Max(count,_instanceCapacity*2+16);GL.BufferData(GL.ARRAY_BUFFER,_instanceCapacity*96,GL.DYNAMIC_DRAW);}
        GL.BufferSubData(GL.ARRAY_BUFFER,0,data,count*24);
        GL.DrawElementsInstanced(GL.TRIANGLES,_count,GL.UNSIGNED_INT,0,count);GL.BindVertexArray(0);
    }
    public void Dispose(){if(_instances!=0)GL.DeleteBuffer(_instances);if(_bones!=0)GL.DeleteTexture(_bones);if(_indices!=0)GL.DeleteBuffer(_indices);if(_vertices!=0)GL.DeleteBuffer(_vertices);if(_vao!=0)GL.DeleteVertexArray(_vao);_instances=_bones=_indices=_vertices=_vao=0;}
}
