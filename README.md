Project Backup from 2022.
Radiance caching for Diffuse GI and Reflections. Using hardware raytracing.
Heavily inspired by UE5's hardware Lumen.

Algorithm uses octaherdral mapping to store occlusion and diffuse bounce in 8x8 directional(configurable in quality settings) pattern in low resolution and jitters direction and location over multiple frames to gather probe data over multiple frames.
Probe data is later filtered using spherical harmonics and interpolated onto screen pixels. 
This is done in screen space(which we need for fine detail) so has alot of spatial instability and still requires a world space radiance cache to be more stable. Additionally the spatiotemporal filter quite incomplete and need to be implemented properly.


![image](https://github.com/user-attachments/assets/93869c77-f21c-4580-bded-d3ca05374cd4)
![image](https://github.com/user-attachments/assets/4452a20a-9761-4cf3-a286-85573bb96196)
![image](https://github.com/user-attachments/assets/e4e7168a-683a-4653-aaa5-d9b5abb2aa05)
![image](https://github.com/user-attachments/assets/0908335b-568d-4d3c-9cec-a023524abe15)
![image](https://github.com/user-attachments/assets/b4465f98-21c9-4657-bcd2-1b8abca6781f)

Screen Space Probes (Octahedral layout):
![image](https://github.com/user-attachments/assets/aef39bc0-538c-4555-81ed-5ff53715e166)

Lighting PDF (Used for importance sampling):
![image](https://github.com/user-attachments/assets/679e310d-7e69-46c7-a1b3-c8c4e6e30f2f)
