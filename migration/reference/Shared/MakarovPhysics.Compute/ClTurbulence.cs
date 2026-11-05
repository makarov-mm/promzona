namespace MakarovPhysics.Compute;

/// <summary>
/// OpenCL backend for TurbulenceLab: turb_cuda.cu mechanically translated -
/// six kernels (MacCormack-free semi-Lagrangian advect with optional
/// diffusion, vorticity, confinement + inflow/walls/solid, divergence, the
/// Jacobi pressure sweep with host-side ping-pong, gradient subtraction),
/// same launch order, same A/B swaps on the host. Eleventh client of
/// <see cref="OpenCl"/>. Never-throw contract.
/// </summary>
public sealed class ClTurbulence : ITurbulenceBackend
{
    private const string Source = @"
#define CELL(x, y, z) (((x) * ny + (y)) * nz + (z))

static float mp_turb_sample(__global const float* restrict f,
    float px, float py, float pz,
    int nx, int ny, int nz, float domX, float domY, float domZ)
{
    float gx = (px / domX + 0.5f) * nx - 0.5f;
    float gy = (py / domY + 0.5f) * ny - 0.5f;
    float gz = (pz / domZ + 0.5f) * nz - 0.5f;
    gx = fmin(fmax(gx, 0.0f), (float)(nx - 1));
    gy = fmin(fmax(gy, 0.0f), (float)(ny - 1));
    gz = fmin(fmax(gz, 0.0f), (float)(nz - 1));
    int x0 = (int)gx, y0 = (int)gy, z0 = (int)gz;
    int x1 = min(x0 + 1, nx - 1), y1 = min(y0 + 1, ny - 1), z1 = min(z0 + 1, nz - 1);
    float fx = gx - x0, fy = gy - y0, fz = gz - z0;

    float c000 = f[CELL(x0, y0, z0)], c100 = f[CELL(x1, y0, z0)];
    float c010 = f[CELL(x0, y1, z0)], c110 = f[CELL(x1, y1, z0)];
    float c001 = f[CELL(x0, y0, z1)], c101 = f[CELL(x1, y0, z1)];
    float c011 = f[CELL(x0, y1, z1)], c111 = f[CELL(x1, y1, z1)];

    float c00 = c000 + (c100 - c000) * fx, c10 = c010 + (c110 - c010) * fx;
    float c01 = c001 + (c101 - c001) * fx, c11 = c011 + (c111 - c011) * fx;
    float c0 = c00 + (c10 - c00) * fy, c1 = c01 + (c11 - c01) * fy;
    return c0 + (c1 - c0) * fz;
}

__kernel void mp_turb_advect(int nx, int ny, int nz, float dt, float diffK,
    float domX, float domY, float domZ,
    __global const float* restrict vxA, __global const float* restrict vyA, __global const float* restrict vzA,
    __global float* restrict vxB, __global float* restrict vyB, __global float* restrict vzB)
{
    int c = get_global_id(0);
    int cells = nx * ny * nz;
    if (c >= cells) return;
    int z = c % nz, y = (c / nz) % ny, x = c / (nz * ny);

    float px = (x + 0.5f) / nx * domX - 0.5f * domX;
    float py = (y + 0.5f) / ny * domY - 0.5f * domY;
    float pz = (z + 0.5f) / nz * domZ - 0.5f * domZ;

    float vx = vxA[c], vy = vyA[c], vz = vzA[c];
    float mx = px - 0.5f * dt * vx, my = py - 0.5f * dt * vy, mz = pz - 0.5f * dt * vz;
    float vmx = mp_turb_sample(vxA, mx, my, mz, nx, ny, nz, domX, domY, domZ);
    float vmy = mp_turb_sample(vyA, mx, my, mz, nx, ny, nz, domX, domY, domZ);
    float vmz = mp_turb_sample(vzA, mx, my, mz, nx, ny, nz, domX, domY, domZ);
    float bx = px - dt * vmx, by = py - dt * vmy, bz = pz - dt * vmz;
    float rx = mp_turb_sample(vxA, bx, by, bz, nx, ny, nz, domX, domY, domZ);
    float ry = mp_turb_sample(vyA, bx, by, bz, nx, ny, nz, domX, domY, domZ);
    float rz = mp_turb_sample(vzA, bx, by, bz, nx, ny, nz, domX, domY, domZ);

    if (diffK > 0.0f)
    {
        int xm = max(x - 1, 0), xp = min(x + 1, nx - 1);
        int ym = max(y - 1, 0), yp = min(y + 1, ny - 1);
        int zm = max(z - 1, 0), zp = min(z + 1, nz - 1);
        rx += diffK * (vxA[CELL(xm, y, z)] + vxA[CELL(xp, y, z)] + vxA[CELL(x, ym, z)]
                     + vxA[CELL(x, yp, z)] + vxA[CELL(x, y, zm)] + vxA[CELL(x, y, zp)] - 6.0f * rx);
        ry += diffK * (vyA[CELL(xm, y, z)] + vyA[CELL(xp, y, z)] + vyA[CELL(x, ym, z)]
                     + vyA[CELL(x, yp, z)] + vyA[CELL(x, y, zm)] + vyA[CELL(x, y, zp)] - 6.0f * ry);
        rz += diffK * (vzA[CELL(xm, y, z)] + vzA[CELL(xp, y, z)] + vzA[CELL(x, ym, z)]
                     + vzA[CELL(x, yp, z)] + vzA[CELL(x, y, zm)] + vzA[CELL(x, y, zp)] - 6.0f * rz);
    }

    vxB[c] = rx; vyB[c] = ry; vzB[c] = rz;
}

__kernel void mp_turb_vorticity(int nx, int ny, int nz, float inv2dx,
    __global const float* restrict vxB, __global const float* restrict vyB, __global const float* restrict vzB,
    __global float* restrict wx, __global float* restrict wy, __global float* restrict wz,
    __global float* restrict wmag)
{
    int c = get_global_id(0);
    int cells = nx * ny * nz;
    if (c >= cells) return;
    int z = c % nz, y = (c / nz) % ny, x = c / (nz * ny);
    int xm = max(x - 1, 0), xp = min(x + 1, nx - 1);
    int ym = max(y - 1, 0), yp = min(y + 1, ny - 1);
    int zm = max(z - 1, 0), zp = min(z + 1, nz - 1);

    float cx = ((vzB[CELL(x, yp, z)] - vzB[CELL(x, ym, z)]) - (vyB[CELL(x, y, zp)] - vyB[CELL(x, y, zm)])) * inv2dx;
    float cy = ((vxB[CELL(x, y, zp)] - vxB[CELL(x, y, zm)]) - (vzB[CELL(xp, y, z)] - vzB[CELL(xm, y, z)])) * inv2dx;
    float cz = ((vyB[CELL(xp, y, z)] - vyB[CELL(xm, y, z)]) - (vxB[CELL(x, yp, z)] - vxB[CELL(x, ym, z)])) * inv2dx;
    wx[c] = cx; wy[c] = cy; wz[c] = cz;
    wmag[c] = sqrt(cx * cx + cy * cy + cz * cz);
}

__kernel void mp_turb_confine(int nx, int ny, int nz, float dt, float eps,
    float dx, float inv2dx, float u0, float time,
    __global const float* restrict vxB, __global const float* restrict vyB, __global const float* restrict vzB,
    __global const float* restrict wx, __global const float* restrict wy, __global const float* restrict wz,
    __global const float* restrict wmag, __global const uchar* restrict solid,
    __global float* restrict vxA, __global float* restrict vyA, __global float* restrict vzA)
{
    int c = get_global_id(0);
    int cells = nx * ny * nz;
    if (c >= cells) return;
    int z = c % nz, y = (c / nz) % ny, x = c / (nz * ny);

    float vx = vxB[c], vy = vyB[c], vz = vzB[c];

    if (eps > 0.0f)
    {
        int xm = max(x - 1, 0), xp = min(x + 1, nx - 1);
        int ym = max(y - 1, 0), yp = min(y + 1, ny - 1);
        int zm = max(z - 1, 0), zp = min(z + 1, nz - 1);
        float ex = (wmag[CELL(xp, y, z)] - wmag[CELL(xm, y, z)]) * inv2dx;
        float ey = (wmag[CELL(x, yp, z)] - wmag[CELL(x, ym, z)]) * inv2dx;
        float ez = (wmag[CELL(x, y, zp)] - wmag[CELL(x, y, zm)]) * inv2dx;
        float n = sqrt(ex * ex + ey * ey + ez * ez) + 1e-5f;
        ex /= n; ey /= n; ez /= n;
        float cwx = wx[c], cwy = wy[c], cwz = wz[c];
        vx += dt * eps * dx * (ey * cwz - ez * cwy);
        vy += dt * eps * dx * (ez * cwx - ex * cwz);
        vz += dt * eps * dx * (ex * cwy - ey * cwx);
    }

    if (x < 2)
    {
        float uz = (z + 0.5f) / nz, uy = (y + 0.5f) / ny;
        vx = u0;
        vy = 0.02f * u0 * sin(6.2832f * (uz * 3.0f + 0.37f * time));
        vz = 0.02f * u0 * sin(6.2832f * (uy * 3.0f - 0.29f * time));
    }

    if (y == 0 || y == ny - 1) vy = 0.0f;
    if (z == 0 || z == nz - 1) vz = 0.0f;
    if (solid[c]) { vx = 0.0f; vy = 0.0f; vz = 0.0f; }

    vxA[c] = vx; vyA[c] = vy; vzA[c] = vz;
}

__kernel void mp_turb_div(int nx, int ny, int nz, float inv2dx,
    __global const float* restrict vxA, __global const float* restrict vyA, __global const float* restrict vzA,
    __global float* restrict divg)
{
    int c = get_global_id(0);
    int cells = nx * ny * nz;
    if (c >= cells) return;
    int z = c % nz, y = (c / nz) % ny, x = c / (nz * ny);
    int xm = max(x - 1, 0), xp = min(x + 1, nx - 1);
    int ym = max(y - 1, 0), yp = min(y + 1, ny - 1);
    int zm = max(z - 1, 0), zp = min(z + 1, nz - 1);
    divg[c] = (vxA[CELL(xp, y, z)] - vxA[CELL(xm, y, z)]
             + vyA[CELL(x, yp, z)] - vyA[CELL(x, ym, z)]
             + vzA[CELL(x, y, zp)] - vzA[CELL(x, y, zm)]) * inv2dx;
}

__kernel void mp_turb_jacobi(int nx, int ny, int nz, float dx2,
    __global const float* restrict pA, __global const float* restrict divg,
    __global float* restrict pB)
{
    int c = get_global_id(0);
    int cells = nx * ny * nz;
    if (c >= cells) return;
    int z = c % nz, y = (c / nz) % ny, x = c / (nz * ny);
    int xm = max(x - 1, 0), xp = min(x + 1, nx - 1);
    int ym = max(y - 1, 0), yp = min(y + 1, ny - 1);
    int zm = max(z - 1, 0), zp = min(z + 1, nz - 1);
    pB[c] = (pA[CELL(xm, y, z)] + pA[CELL(xp, y, z)]
           + pA[CELL(x, ym, z)] + pA[CELL(x, yp, z)]
           + pA[CELL(x, y, zm)] + pA[CELL(x, y, zp)]
           - divg[c] * dx2) / 6.0f;
}

__kernel void mp_turb_gradient(int nx, int ny, int nz, float inv2dx,
    __global const float* restrict vxA, __global const float* restrict vyA, __global const float* restrict vzA,
    __global const float* restrict pA, __global const uchar* restrict solid,
    __global float* restrict vxB, __global float* restrict vyB, __global float* restrict vzB)
{
    int c = get_global_id(0);
    int cells = nx * ny * nz;
    if (c >= cells) return;
    int z = c % nz, y = (c / nz) % ny, x = c / (nz * ny);
    int xm = max(x - 1, 0), xp = min(x + 1, nx - 1);
    int ym = max(y - 1, 0), yp = min(y + 1, ny - 1);
    int zm = max(z - 1, 0), zp = min(z + 1, nz - 1);
    float vx = vxA[c] - (pA[CELL(xp, y, z)] - pA[CELL(xm, y, z)]) * inv2dx;
    float vy = vyA[c] - (pA[CELL(x, yp, z)] - pA[CELL(x, ym, z)]) * inv2dx;
    float vz = vzA[c] - (pA[CELL(x, y, zp)] - pA[CELL(x, y, zm)]) * inv2dx;
    if (solid[c]) { vx = 0.0f; vy = 0.0f; vz = 0.0f; }
    vxB[c] = vx; vyB[c] = vy; vzB[c] = vz;
}

/* ---- sub-grid closures (26.08): mirrors of TurbEddyKernel / TurbDiffuseKernel ---- */
__kernel void mp_turb_eddy(int nx, int ny, int nz, int model, float coef, float inv2dx,
    __global const float* restrict vxA, __global const float* restrict vyA,
    __global const float* restrict vzA, __global const uchar* restrict solid,
    __global float* restrict nut)
{
    int c = get_global_id(0);
    int cells = nx * ny * nz;
    if (c >= cells) return;
    if (solid[c]) { nut[c] = 0.0f; return; }
    int z = c % nz, y = (c / nz) % ny, x = c / (nz * ny);
    int xm = max(x - 1, 0), xp = min(x + 1, nx - 1);
    int ym = max(y - 1, 0), yp = min(y + 1, ny - 1);
    int zm = max(z - 1, 0), zp = min(z + 1, nz - 1);
    int cxm = CELL(xm, y, z), cxp = CELL(xp, y, z);
    int cym = CELL(x, ym, z), cyp = CELL(x, yp, z);
    int czm = CELL(x, y, zm), czp = CELL(x, y, zp);
    float g[9];
    g[0] = (vxA[cxp] - vxA[cxm]) * inv2dx; g[1] = (vxA[cyp] - vxA[cym]) * inv2dx; g[2] = (vxA[czp] - vxA[czm]) * inv2dx;
    g[3] = (vyA[cxp] - vyA[cxm]) * inv2dx; g[4] = (vyA[cyp] - vyA[cym]) * inv2dx; g[5] = (vyA[czp] - vyA[czm]) * inv2dx;
    g[6] = (vzA[cxp] - vzA[cxm]) * inv2dx; g[7] = (vzA[cyp] - vzA[cym]) * inv2dx; g[8] = (vzA[czp] - vzA[czm]) * inv2dx;
    float ss = 0.0f;
    for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
    { float sij = 0.5f * (g[i * 3 + j] + g[j * 3 + i]); ss += sij * sij; }
    float v;
    if (model == 1)
    {
        v = coef * sqrt(2.0f * ss);
    }
    else
    {
        float g2[9];
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
        { float s = 0.0f; for (int k = 0; k < 3; k++) s += g[i * 3 + k] * g[k * 3 + j]; g2[i * 3 + j] = s; }
        float tr = (g2[0] + g2[4] + g2[8]) / 3.0f;
        float sdsd = 0.0f;
        for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
        { float sd = 0.5f * (g2[i * 3 + j] + g2[j * 3 + i]) - (i == j ? tr : 0.0f); sdsd += sd * sd; }
        float denom = pow(ss, 2.5f) + pow(sdsd, 1.25f);
        v = denom > 1e-30f ? coef * pow(sdsd, 1.5f) / denom : 0.0f;
    }
    nut[c] = v;
}

__kernel void mp_turb_diffuse(int nx, int ny, int nz, float nuMol, float dtdx2,
    __global const float* restrict vxA, __global const float* restrict vyA,
    __global const float* restrict vzA, __global const float* restrict nut,
    __global float* restrict vxB, __global float* restrict vyB, __global float* restrict vzB)
{
    int c = get_global_id(0);
    int cells = nx * ny * nz;
    if (c >= cells) return;
    int z = c % nz, y = (c / nz) % ny, x = c / (nz * ny);
    int n[6] = { CELL(max(x - 1, 0), y, z), CELL(min(x + 1, nx - 1), y, z),
                 CELL(x, max(y - 1, 0), z), CELL(x, min(y + 1, ny - 1), z),
                 CELL(x, y, max(z - 1, 0)), CELL(x, y, min(z + 1, nz - 1)) };
    float bx = vxB[c], by = vyB[c], bz = vzB[c];
    float nuc = nuMol + nut[c];
    float sx = 0.0f, sy = 0.0f, sz = 0.0f;
    for (int f = 0; f < 6; f++)
    {
        float k = fmin(0.5f * (nuc + nuMol + nut[n[f]]) * dtdx2, 0.16f);
        sx += k * (vxA[n[f]] - bx); sy += k * (vyA[n[f]] - by); sz += k * (vzA[n[f]] - bz);
    }
    vxB[c] = bx + sx; vyB[c] = by + sy; vzB[c] = bz + sz;
}

/* ---- k-omega SST (tail421): mirrors of the CUDA SST kernels ---- */
#define SST_SK1 0.85f
#define SST_SW1 0.5f
#define SST_B1 0.075f
#define SST_G1 (5.0f / 9.0f)
#define SST_SK2 1.0f
#define SST_SW2 0.856f
#define SST_B2 0.0828f
#define SST_G2 0.44f
#define SST_BSTAR 0.09f
#define SST_A1 0.31f
#define SST_KMIN 1e-10f
#define SST_WMIN 1e-6f
#define DDES_C1 0.78f
#define DDES_C2 0.61f
#define DDES_CD1 20.0f
#define DDES_CD2 3.0f
#define DDES_KAPPA 0.41f

__kernel void mp_turb_eddy_sst(int nx, int ny, int nz, float nuMol, float dx, float nutCeil, float inv2dx,
    __global const float* restrict vxA, __global const float* restrict vyA, __global const float* restrict vzA,
    __global const float* restrict kA, __global const float* restrict wA, __global const float* restrict wallDist,
    __global const uchar* restrict solid, __global float* restrict nut, __global float* restrict smagOut,
    __global float* restrict gmagOut)
{
    int c = get_global_id(0);
    int cells = nx * ny * nz;
    if (c >= cells) return;
    if (solid[c]) { nut[c] = 0.0f; smagOut[c] = 0.0f; gmagOut[c] = 0.0f; return; }
    int z = c % nz, y = (c / nz) % ny, x = c / (nz * ny);
    int xm = max(x - 1, 0), xp = min(x + 1, nx - 1);
    int ym = max(y - 1, 0), yp = min(y + 1, ny - 1);
    int zm = max(z - 1, 0), zp = min(z + 1, nz - 1);
    int cxm = CELL(xm, y, z), cxp = CELL(xp, y, z);
    int cym = CELL(x, ym, z), cyp = CELL(x, yp, z);
    int czm = CELL(x, y, zm), czp = CELL(x, y, zp);
    float g[9];
    g[0] = (vxA[cxp] - vxA[cxm]) * inv2dx; g[1] = (vxA[cyp] - vxA[cym]) * inv2dx; g[2] = (vxA[czp] - vxA[czm]) * inv2dx;
    g[3] = (vyA[cxp] - vyA[cxm]) * inv2dx; g[4] = (vyA[cyp] - vyA[cym]) * inv2dx; g[5] = (vyA[czp] - vyA[czm]) * inv2dx;
    g[6] = (vzA[cxp] - vzA[cxm]) * inv2dx; g[7] = (vzA[cyp] - vzA[cym]) * inv2dx; g[8] = (vzA[czp] - vzA[czm]) * inv2dx;
    float ss = 0.0f;
    for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++)
    { float sij = 0.5f * (g[i * 3 + j] + g[j * 3 + i]); ss += sij * sij; }
    float smag = sqrt(2.0f * ss);
    float k = fmax(kA[c], SST_KMIN), w = fmax(wA[c], SST_WMIN);
    float yw = fmax(wallDist[c], 0.5f * dx);
    float arg2 = fmax(2.0f * sqrt(k) / (SST_BSTAR * w * yw), 500.0f * nuMol / (yw * yw * w));
    float f2 = tanh(arg2 * arg2);
    float v = SST_A1 * k / fmax(SST_A1 * w, smag * f2);
    nut[c] = fmin(v, nutCeil);
    smagOut[c] = smag;
    float gg = 0.0f;
    for (int i = 0; i < 9; i++) gg += g[i] * g[i];
    gmagOut[c] = sqrt(gg);
}

__kernel void mp_turb_advect_scalar(int nx, int ny, int nz, float dt,
    float domX, float domY, float domZ,
    __global const float* restrict vxA, __global const float* restrict vyA, __global const float* restrict vzA,
    __global const float* restrict kA, __global const float* restrict wA,
    __global float* restrict kB, __global float* restrict wB)
{
    int c = get_global_id(0);
    int cells = nx * ny * nz;
    if (c >= cells) return;
    int z = c % nz, y = (c / nz) % ny, x = c / (nz * ny);
    float px = (x + 0.5f) / nx * domX - 0.5f * domX;
    float py = (y + 0.5f) / ny * domY - 0.5f * domY;
    float pz = (z + 0.5f) / nz * domZ - 0.5f * domZ;
    float vx = vxA[c], vy = vyA[c], vz = vzA[c];
    float mx = px - 0.5f * dt * vx, my = py - 0.5f * dt * vy, mz = pz - 0.5f * dt * vz;
    float vmx = mp_turb_sample(vxA, mx, my, mz, nx, ny, nz, domX, domY, domZ);
    float vmy = mp_turb_sample(vyA, mx, my, mz, nx, ny, nz, domX, domY, domZ);
    float vmz = mp_turb_sample(vzA, mx, my, mz, nx, ny, nz, domX, domY, domZ);
    float bx = px - dt * vmx, by = py - dt * vmy, bz = pz - dt * vmz;
    kB[c] = mp_turb_sample(kA, bx, by, bz, nx, ny, nz, domX, domY, domZ);
    wB[c] = mp_turb_sample(wA, bx, by, bz, nx, ny, nz, domX, domY, domZ);
}

__kernel void mp_turb_sst_transport(int nx, int ny, int nz, float dt, float dtdx2, float inv2dx,
    float nuMol, float viscosity, float dx, float kIn, float wIn, float wWall, int ddes,
    __global const float* restrict kA, __global const float* restrict wA,
    __global const float* restrict kB, __global const float* restrict wB,
    __global const float* restrict nut, __global const float* restrict smagIn,
    __global const float* restrict gmagIn,
    __global const float* restrict wallDist, __global const uchar* restrict solid,
    __global float* restrict kC, __global float* restrict wC)
{
    int c = get_global_id(0);
    int cells = nx * ny * nz;
    if (c >= cells) return;
    int z = c % nz, y = (c / nz) % ny, x = c / (nz * ny);
    if (solid[c]) { kC[c] = 0.0f; wC[c] = wWall; return; }
    if (x < 2) { kC[c] = kIn; wC[c] = wIn; return; }
    int xm = max(x - 1, 0), xp = min(x + 1, nx - 1);
    int ym = max(y - 1, 0), yp = min(y + 1, ny - 1);
    int zm = max(z - 1, 0), zp = min(z + 1, nz - 1);
    int n[6] = { CELL(xm, y, z), CELL(xp, y, z), CELL(x, ym, z), CELL(x, yp, z), CELL(x, y, zm), CELL(x, y, zp) };
    float k = fmax(kA[c], SST_KMIN), w = fmax(wA[c], SST_WMIN);
    float nt = nut[c];
    float smag = smagIn[c];
    float yw = fmax(wallDist[c], 0.5f * dx);
    float dkx = (kA[n[1]] - kA[n[0]]) * inv2dx, dky = (kA[n[3]] - kA[n[2]]) * inv2dx, dkz = (kA[n[5]] - kA[n[4]]) * inv2dx;
    float dwx = (wA[n[1]] - wA[n[0]]) * inv2dx, dwy = (wA[n[3]] - wA[n[2]]) * inv2dx, dwz = (wA[n[5]] - wA[n[4]]) * inv2dx;
    float gradKW = dkx * dwx + dky * dwy + dkz * dwz;
    float cdkw = fmax(2.0f * SST_SW2 / w * gradKW, 1e-10f);
    float arg1 = fmin(fmax(sqrt(k) / (SST_BSTAR * w * yw), 500.0f * nuMol / (yw * yw * w)),
                      4.0f * SST_SW2 * k / (cdkw * yw * yw));
    float f1 = tanh(arg1 * arg1 * arg1 * arg1);
    float sigK = f1 * SST_SK1 + (1.0f - f1) * SST_SK2;
    float sigW = f1 * SST_SW1 + (1.0f - f1) * SST_SW2;
    float beta = f1 * SST_B1 + (1.0f - f1) * SST_B2;
    float gamma = f1 * SST_G1 + (1.0f - f1) * SST_G2;
    float kStar = kB[c], wStar = wB[c];
    float dk = 0.0f, dw = 0.0f;
    for (int f = 0; f < 6; f++)
    {
        float nn = nut[n[f]];
        float ck = fmin((viscosity + 0.5f * sigK * (nt + nn)) * dtdx2, 0.16f);
        float cw = fmin((viscosity + 0.5f * sigW * (nt + nn)) * dtdx2, 0.16f);
        dk += ck * (kA[n[f]] - kStar);
        dw += cw * (wA[n[f]] - wStar);
    }
    kStar += dk; wStar += dw;
    float prodK = fmin(nt * smag * smag, 10.0f * SST_BSTAR * k * w);
    float prodW = gamma * smag * smag;
    float cross = (1.0f - f1) * 2.0f * SST_SW2 / w * fmax(gradKW, 0.0f);
    float fDdes = 1.0f;
    if (ddes)
    {
        float lRans = sqrt(k) / (SST_BSTAR * w);
        float cDes = f1 * DDES_C1 + (1.0f - f1) * DDES_C2;
        float rd = (nt + nuMol) / (DDES_KAPPA * DDES_KAPPA * yw * yw * fmax(gmagIn[c], 1e-10f));
        float fd = 1.0f - tanh(pow(DDES_CD1 * rd, DDES_CD2));
        float lDdes = lRans - fd * fmax(0.0f, lRans - cDes * dx);
        fDdes = lRans / fmax(lDdes, 1e-12f);
    }
    float kNew = (kStar + dt * prodK) / (1.0f + dt * SST_BSTAR * w * fDdes);
    float wNew = (wStar + dt * (prodW + cross)) / (1.0f + dt * beta * w);
    kC[c] = fmax(kNew, SST_KMIN);
    wC[c] = fmax(wNew, SST_WMIN);
}
";

    private IntPtr _program, _kAdvect, _kVort, _kConfine, _kDiv, _kJacobi, _kGrad, _kEddy, _kDiffuse, _kEddySst, _kAdvScalar, _kSst;
    private IntPtr _vxA, _vyA, _vzA, _vxB, _vyB, _vzB, _pA, _pB, _div, _wx, _wy, _wz, _wmag, _solid, _nut;
    private IntPtr _kA, _kB, _kC, _wA, _wB, _wC, _smag, _gmag, _wallDist;   // SST (tail421), DDES (tail422)
    private int _capacity;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static ClTurbulence? TryCreate(int maxCells)
    {
        if (maxCells <= 0 || !OpenCl.Available) return null;
        var b = new ClTurbulence();
        try
        {
            b._program = OpenCl.BuildProgram(Source, out _);
            if (b._program == IntPtr.Zero) { b.Dispose(); return null; }
            b._kAdvect = OpenCl.Kernel(b._program, "mp_turb_advect");
            b._kVort = OpenCl.Kernel(b._program, "mp_turb_vorticity");
            b._kConfine = OpenCl.Kernel(b._program, "mp_turb_confine");
            b._kDiv = OpenCl.Kernel(b._program, "mp_turb_div");
            b._kJacobi = OpenCl.Kernel(b._program, "mp_turb_jacobi");
            b._kGrad = OpenCl.Kernel(b._program, "mp_turb_gradient");
            b._kEddy = OpenCl.Kernel(b._program, "mp_turb_eddy");
            b._kDiffuse = OpenCl.Kernel(b._program, "mp_turb_diffuse");
            b._kEddySst = OpenCl.Kernel(b._program, "mp_turb_eddy_sst");
            b._kAdvScalar = OpenCl.Kernel(b._program, "mp_turb_advect_scalar");
            b._kSst = OpenCl.Kernel(b._program, "mp_turb_sst_transport");
            if (b._kAdvect == IntPtr.Zero || b._kVort == IntPtr.Zero || b._kConfine == IntPtr.Zero
                || b._kDiv == IntPtr.Zero || b._kJacobi == IntPtr.Zero || b._kGrad == IntPtr.Zero
                || b._kEddy == IntPtr.Zero || b._kDiffuse == IntPtr.Zero
                || b._kEddySst == IntPtr.Zero || b._kAdvScalar == IntPtr.Zero || b._kSst == IntPtr.Zero)
            { b.Dispose(); return null; }
            long cb = 4L * maxCells;
            b._vxA = OpenCl.Buffer(cb); b._vyA = OpenCl.Buffer(cb); b._vzA = OpenCl.Buffer(cb);
            b._vxB = OpenCl.Buffer(cb); b._vyB = OpenCl.Buffer(cb); b._vzB = OpenCl.Buffer(cb);
            b._pA = OpenCl.Buffer(cb); b._pB = OpenCl.Buffer(cb); b._div = OpenCl.Buffer(cb);
            b._wx = OpenCl.Buffer(cb); b._wy = OpenCl.Buffer(cb); b._wz = OpenCl.Buffer(cb);
            b._wmag = OpenCl.Buffer(cb);
            b._nut = OpenCl.Buffer(cb);
            b._kA = OpenCl.Buffer(cb); b._kB = OpenCl.Buffer(cb); b._kC = OpenCl.Buffer(cb);
            b._wA = OpenCl.Buffer(cb); b._wB = OpenCl.Buffer(cb); b._wC = OpenCl.Buffer(cb);
            b._smag = OpenCl.Buffer(cb); b._gmag = OpenCl.Buffer(cb); b._wallDist = OpenCl.Buffer(cb);
            b._solid = OpenCl.Buffer(maxCells);
            if (b._vxA == IntPtr.Zero || b._vyA == IntPtr.Zero || b._vzA == IntPtr.Zero
                || b._vxB == IntPtr.Zero || b._vyB == IntPtr.Zero || b._vzB == IntPtr.Zero
                || b._pA == IntPtr.Zero || b._pB == IntPtr.Zero || b._div == IntPtr.Zero
                || b._wx == IntPtr.Zero || b._wy == IntPtr.Zero || b._wz == IntPtr.Zero
                || b._wmag == IntPtr.Zero || b._nut == IntPtr.Zero || b._solid == IntPtr.Zero
                || b._kA == IntPtr.Zero || b._kB == IntPtr.Zero || b._kC == IntPtr.Zero
                || b._wA == IntPtr.Zero || b._wB == IntPtr.Zero || b._wC == IntPtr.Zero
                || b._smag == IntPtr.Zero || b._gmag == IntPtr.Zero || b._wallDist == IntPtr.Zero) { b.Dispose(); return null; }
            // ν_t starts at zero so a Dns download reads a clean field
            OpenCl.Write(b._nut, new float[maxCells], maxCells);
            b._capacity = maxCells;
            return b;
        }
        catch
        {
            b.Dispose();
            return null;
        }
    }

    private ClTurbulence() { }

    public bool Upload(int cells, float[] vx, float[] vy, float[] vz, float[] p, byte[] solid)
    {
        if (_kAdvect == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (vx is null || vx.Length < cells || vy is null || vy.Length < cells
            || vz is null || vz.Length < cells || p is null || p.Length < cells
            || solid is null || solid.Length < cells) return false;
        try
        {
            return OpenCl.Write(_vxA, vx, cells) && OpenCl.Write(_vyA, vy, cells)
                && OpenCl.Write(_vzA, vz, cells) && OpenCl.Write(_pA, p, cells)
                && OpenCl.Write(_solid, solid, cells);
        }
        catch { return false; }
    }

    public bool UploadScalars(int cells, float[] k, float[] w, float[] wallDist)
    {
        if (_kAdvect == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (k is null || k.Length < cells || w is null || w.Length < cells || wallDist is null || wallDist.Length < cells) return false;
        try { return OpenCl.Write(_kA, k, cells) && OpenCl.Write(_wA, w, cells) && OpenCl.Write(_wallDist, wallDist, cells); }
        catch { return false; }
    }

    public bool UploadSolid(int cells, byte[] solid)
    {
        if (_kAdvect == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (solid is null || solid.Length < cells) return false;
        try { return OpenCl.Write(_solid, solid, cells); }
        catch { return false; }
    }

    public bool Step(int nx, int ny, int nz, float dt, float diffK, float confinement,
        float dx, float flowSpeed, float time, float domX, float domY, float domZ, int jacobiIters,
        int sgsModel, float sgsCoef, float viscosity, float kIn, float wIn, float wWall)
    {
        long cells = (long)nx * ny * nz;
        if (_kAdvect == IntPtr.Zero || cells <= 0 || cells > _capacity || jacobiIters <= 0) return false;
        try
        {
            float inv2dx = 0.5f / dx;
            bool les = sgsModel != 0;
            bool sst = sgsModel == 3 || sgsModel == 4;
            float nuMol = Math.Max(viscosity, 1e-6f);
            float dtdx2 = dt / (dx * dx);

            if (sst)
            {
                bool okE = OpenCl.Arg(_kEddySst, 0, nx) && OpenCl.Arg(_kEddySst, 1, ny) &&
                    OpenCl.Arg(_kEddySst, 2, nz) && OpenCl.Arg(_kEddySst, 3, nuMol) &&
                    OpenCl.Arg(_kEddySst, 4, dx) && OpenCl.Arg(_kEddySst, 5, 0.32f * dx * dx / Math.Max(dt, 1e-4f)) &&
                    OpenCl.Arg(_kEddySst, 6, inv2dx) &&
                    OpenCl.Arg(_kEddySst, 7, _vxA) && OpenCl.Arg(_kEddySst, 8, _vyA) &&
                    OpenCl.Arg(_kEddySst, 9, _vzA) && OpenCl.Arg(_kEddySst, 10, _kA) &&
                    OpenCl.Arg(_kEddySst, 11, _wA) && OpenCl.Arg(_kEddySst, 12, _wallDist) &&
                    OpenCl.Arg(_kEddySst, 13, _solid) && OpenCl.Arg(_kEddySst, 14, _nut) &&
                    OpenCl.Arg(_kEddySst, 15, _smag) && OpenCl.Arg(_kEddySst, 16, _gmag) &&
                    OpenCl.Launch(_kEddySst, cells);
                if (!okE) return false;
            }
            else if (les)
            {
                bool okE = OpenCl.Arg(_kEddy, 0, nx) && OpenCl.Arg(_kEddy, 1, ny) &&
                    OpenCl.Arg(_kEddy, 2, nz) && OpenCl.Arg(_kEddy, 3, sgsModel) &&
                    OpenCl.Arg(_kEddy, 4, sgsCoef) && OpenCl.Arg(_kEddy, 5, inv2dx) &&
                    OpenCl.Arg(_kEddy, 6, _vxA) && OpenCl.Arg(_kEddy, 7, _vyA) &&
                    OpenCl.Arg(_kEddy, 8, _vzA) && OpenCl.Arg(_kEddy, 9, _solid) &&
                    OpenCl.Arg(_kEddy, 10, _nut) &&
                    OpenCl.Launch(_kEddy, cells);
                if (!okE) return false;
            }

            bool ok = OpenCl.Arg(_kAdvect, 0, nx) && OpenCl.Arg(_kAdvect, 1, ny) &&
                OpenCl.Arg(_kAdvect, 2, nz) && OpenCl.Arg(_kAdvect, 3, dt) &&
                OpenCl.Arg(_kAdvect, 4, les ? 0.0f : diffK) &&
                OpenCl.Arg(_kAdvect, 5, domX) && OpenCl.Arg(_kAdvect, 6, domY) &&
                OpenCl.Arg(_kAdvect, 7, domZ) &&
                OpenCl.Arg(_kAdvect, 8, _vxA) && OpenCl.Arg(_kAdvect, 9, _vyA) &&
                OpenCl.Arg(_kAdvect, 10, _vzA) &&
                OpenCl.Arg(_kAdvect, 11, _vxB) && OpenCl.Arg(_kAdvect, 12, _vyB) &&
                OpenCl.Arg(_kAdvect, 13, _vzB) &&
                OpenCl.Launch(_kAdvect, cells);
            if (!ok) return false;

            if (sst)
            {
                ok = OpenCl.Arg(_kAdvScalar, 0, nx) && OpenCl.Arg(_kAdvScalar, 1, ny) &&
                    OpenCl.Arg(_kAdvScalar, 2, nz) && OpenCl.Arg(_kAdvScalar, 3, dt) &&
                    OpenCl.Arg(_kAdvScalar, 4, domX) && OpenCl.Arg(_kAdvScalar, 5, domY) &&
                    OpenCl.Arg(_kAdvScalar, 6, domZ) &&
                    OpenCl.Arg(_kAdvScalar, 7, _vxA) && OpenCl.Arg(_kAdvScalar, 8, _vyA) &&
                    OpenCl.Arg(_kAdvScalar, 9, _vzA) && OpenCl.Arg(_kAdvScalar, 10, _kA) &&
                    OpenCl.Arg(_kAdvScalar, 11, _wA) && OpenCl.Arg(_kAdvScalar, 12, _kB) &&
                    OpenCl.Arg(_kAdvScalar, 13, _wB) &&
                    OpenCl.Launch(_kAdvScalar, cells);
                if (!ok) return false;
            }

            if (les)
            {
                ok = OpenCl.Arg(_kDiffuse, 0, nx) && OpenCl.Arg(_kDiffuse, 1, ny) &&
                    OpenCl.Arg(_kDiffuse, 2, nz) && OpenCl.Arg(_kDiffuse, 3, viscosity) &&
                    OpenCl.Arg(_kDiffuse, 4, dt / (dx * dx)) &&
                    OpenCl.Arg(_kDiffuse, 5, _vxA) && OpenCl.Arg(_kDiffuse, 6, _vyA) &&
                    OpenCl.Arg(_kDiffuse, 7, _vzA) && OpenCl.Arg(_kDiffuse, 8, _nut) &&
                    OpenCl.Arg(_kDiffuse, 9, _vxB) && OpenCl.Arg(_kDiffuse, 10, _vyB) &&
                    OpenCl.Arg(_kDiffuse, 11, _vzB) &&
                    OpenCl.Launch(_kDiffuse, cells);
                if (!ok) return false;
            }

            if (sst)
            {
                ok = OpenCl.Arg(_kSst, 0, nx) && OpenCl.Arg(_kSst, 1, ny) &&
                    OpenCl.Arg(_kSst, 2, nz) && OpenCl.Arg(_kSst, 3, dt) &&
                    OpenCl.Arg(_kSst, 4, dtdx2) && OpenCl.Arg(_kSst, 5, inv2dx) &&
                    OpenCl.Arg(_kSst, 6, nuMol) && OpenCl.Arg(_kSst, 7, viscosity) &&
                    OpenCl.Arg(_kSst, 8, dx) && OpenCl.Arg(_kSst, 9, kIn) &&
                    OpenCl.Arg(_kSst, 10, wIn) && OpenCl.Arg(_kSst, 11, wWall) &&
                    OpenCl.Arg(_kSst, 12, sgsModel == 4 ? 1 : 0) &&
                    OpenCl.Arg(_kSst, 13, _kA) && OpenCl.Arg(_kSst, 14, _wA) &&
                    OpenCl.Arg(_kSst, 15, _kB) && OpenCl.Arg(_kSst, 16, _wB) &&
                    OpenCl.Arg(_kSst, 17, _nut) && OpenCl.Arg(_kSst, 18, _smag) &&
                    OpenCl.Arg(_kSst, 19, _gmag) &&
                    OpenCl.Arg(_kSst, 20, _wallDist) && OpenCl.Arg(_kSst, 21, _solid) &&
                    OpenCl.Arg(_kSst, 22, _kC) && OpenCl.Arg(_kSst, 23, _wC) &&
                    OpenCl.Launch(_kSst, cells);
                if (!ok) return false;
                (_kA, _kC) = (_kC, _kA);
                (_wA, _wC) = (_wC, _wA);
            }

            ok = OpenCl.Arg(_kVort, 0, nx) && OpenCl.Arg(_kVort, 1, ny) &&
                OpenCl.Arg(_kVort, 2, nz) && OpenCl.Arg(_kVort, 3, inv2dx) &&
                OpenCl.Arg(_kVort, 4, _vxB) && OpenCl.Arg(_kVort, 5, _vyB) &&
                OpenCl.Arg(_kVort, 6, _vzB) &&
                OpenCl.Arg(_kVort, 7, _wx) && OpenCl.Arg(_kVort, 8, _wy) &&
                OpenCl.Arg(_kVort, 9, _wz) && OpenCl.Arg(_kVort, 10, _wmag) &&
                OpenCl.Launch(_kVort, cells);
            if (!ok) return false;

            ok = OpenCl.Arg(_kConfine, 0, nx) && OpenCl.Arg(_kConfine, 1, ny) &&
                OpenCl.Arg(_kConfine, 2, nz) && OpenCl.Arg(_kConfine, 3, dt) &&
                OpenCl.Arg(_kConfine, 4, confinement) && OpenCl.Arg(_kConfine, 5, dx) &&
                OpenCl.Arg(_kConfine, 6, inv2dx) && OpenCl.Arg(_kConfine, 7, flowSpeed) &&
                OpenCl.Arg(_kConfine, 8, time) &&
                OpenCl.Arg(_kConfine, 9, _vxB) && OpenCl.Arg(_kConfine, 10, _vyB) &&
                OpenCl.Arg(_kConfine, 11, _vzB) &&
                OpenCl.Arg(_kConfine, 12, _wx) && OpenCl.Arg(_kConfine, 13, _wy) &&
                OpenCl.Arg(_kConfine, 14, _wz) && OpenCl.Arg(_kConfine, 15, _wmag) &&
                OpenCl.Arg(_kConfine, 16, _solid) &&
                OpenCl.Arg(_kConfine, 17, _vxA) && OpenCl.Arg(_kConfine, 18, _vyA) &&
                OpenCl.Arg(_kConfine, 19, _vzA) &&
                OpenCl.Launch(_kConfine, cells);
            if (!ok) return false;

            ok = OpenCl.Arg(_kDiv, 0, nx) && OpenCl.Arg(_kDiv, 1, ny) &&
                OpenCl.Arg(_kDiv, 2, nz) && OpenCl.Arg(_kDiv, 3, inv2dx) &&
                OpenCl.Arg(_kDiv, 4, _vxA) && OpenCl.Arg(_kDiv, 5, _vyA) &&
                OpenCl.Arg(_kDiv, 6, _vzA) && OpenCl.Arg(_kDiv, 7, _div) &&
                OpenCl.Launch(_kDiv, cells);
            if (!ok) return false;

            for (int it = 0; it < jacobiIters; it++)
            {
                ok = OpenCl.Arg(_kJacobi, 0, nx) && OpenCl.Arg(_kJacobi, 1, ny) &&
                    OpenCl.Arg(_kJacobi, 2, nz) && OpenCl.Arg(_kJacobi, 3, dx * dx) &&
                    OpenCl.Arg(_kJacobi, 4, _pA) && OpenCl.Arg(_kJacobi, 5, _div) &&
                    OpenCl.Arg(_kJacobi, 6, _pB) &&
                    OpenCl.Launch(_kJacobi, cells);
                if (!ok) return false;
                (_pA, _pB) = (_pB, _pA);
            }

            ok = OpenCl.Arg(_kGrad, 0, nx) && OpenCl.Arg(_kGrad, 1, ny) &&
                OpenCl.Arg(_kGrad, 2, nz) && OpenCl.Arg(_kGrad, 3, inv2dx) &&
                OpenCl.Arg(_kGrad, 4, _vxA) && OpenCl.Arg(_kGrad, 5, _vyA) &&
                OpenCl.Arg(_kGrad, 6, _vzA) && OpenCl.Arg(_kGrad, 7, _pA) &&
                OpenCl.Arg(_kGrad, 8, _solid) &&
                OpenCl.Arg(_kGrad, 9, _vxB) && OpenCl.Arg(_kGrad, 10, _vyB) &&
                OpenCl.Arg(_kGrad, 11, _vzB) &&
                OpenCl.Launch(_kGrad, cells);
            if (!ok) return false;

            // the corrected field becomes current: swap A and B like CUDA does
            (_vxA, _vxB) = (_vxB, _vxA);
            (_vyA, _vyB) = (_vyB, _vyA);
            (_vzA, _vzB) = (_vzB, _vzA);

            return OpenCl.Finish();
        }
        catch { return false; }
    }

    public bool Download(int cells, float[]? vx, float[]? vy, float[]? vz, float[]? wmag, float[]? nut, float[]? k, float[]? w, float[]? p)
    {
        if (k is not null && k.Length < cells) return false;
        if (p is not null && p.Length < cells) return false;
        if (w is not null && w.Length < cells) return false;
        if (_kAdvect == IntPtr.Zero || cells <= 0 || cells > _capacity) return false;
        if (vx is not null && vx.Length < cells) return false;
        if (vy is not null && vy.Length < cells) return false;
        if (vz is not null && vz.Length < cells) return false;
        if (wmag is not null && wmag.Length < cells) return false;
        if (nut is not null && nut.Length < cells) return false;
        try
        {
            if (vx is not null && !OpenCl.Read(_vxA, vx, cells)) return false;
            if (vy is not null && !OpenCl.Read(_vyA, vy, cells)) return false;
            if (vz is not null && !OpenCl.Read(_vzA, vz, cells)) return false;
            if (wmag is not null && !OpenCl.Read(_wmag, wmag, cells)) return false;
            if (nut is not null && !OpenCl.Read(_nut, nut, cells)) return false;
            if (k is not null && !OpenCl.Read(_kA, k, cells)) return false;
            if (w is not null && !OpenCl.Read(_wA, w, cells)) return false;
            if (p is not null && !OpenCl.Read(_pA, p, cells)) return false;
            return true;
        }
        catch { return false; }
    }

    public void Dispose()
    {
        OpenCl.Release(_vxA); OpenCl.Release(_vyA); OpenCl.Release(_vzA);
        OpenCl.Release(_vxB); OpenCl.Release(_vyB); OpenCl.Release(_vzB);
        OpenCl.Release(_pA); OpenCl.Release(_pB); OpenCl.Release(_div);
        OpenCl.Release(_wx); OpenCl.Release(_wy); OpenCl.Release(_wz); OpenCl.Release(_wmag); OpenCl.Release(_nut);
        OpenCl.Release(_kA); OpenCl.Release(_kB); OpenCl.Release(_kC); OpenCl.Release(_wA); OpenCl.Release(_wB); OpenCl.Release(_wC);
        OpenCl.Release(_smag); OpenCl.Release(_gmag); OpenCl.Release(_wallDist);
        _kA = _kB = _kC = _wA = _wB = _wC = _smag = _gmag = _wallDist = IntPtr.Zero;
        OpenCl.Release(_solid);
        _vxA = _vyA = _vzA = _vxB = _vyB = _vzB = _pA = _pB = _div = IntPtr.Zero;
        _wx = _wy = _wz = _wmag = _nut = _solid = IntPtr.Zero;
        OpenCl.ReleaseKernel(_kAdvect); OpenCl.ReleaseKernel(_kVort); OpenCl.ReleaseKernel(_kConfine);
        OpenCl.ReleaseKernel(_kDiv); OpenCl.ReleaseKernel(_kJacobi); OpenCl.ReleaseKernel(_kGrad);
        OpenCl.ReleaseKernel(_kEddy); OpenCl.ReleaseKernel(_kDiffuse);
        OpenCl.ReleaseKernel(_kEddySst); OpenCl.ReleaseKernel(_kAdvScalar); OpenCl.ReleaseKernel(_kSst);
        _kAdvect = _kVort = _kConfine = _kDiv = _kJacobi = _kGrad = _kEddy = _kDiffuse = IntPtr.Zero;
        _kEddySst = _kAdvScalar = _kSst = IntPtr.Zero;
        OpenCl.ReleaseProgram(_program);
        _program = IntPtr.Zero;
    }
}
