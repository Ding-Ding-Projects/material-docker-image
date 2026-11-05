// Real output captured on 2026-11-05 from Docker Desktop 29.6.1 with Docker Scout 1.22.0 (NO_COLOR=1):
//   docker scout recommendations httpd:2.4 | itzg/minecraft-server:java21 | nginx:alpine | debian:bookworm-slim | does-not-exist-xyz:1
// and from a BuildKit build of the probe Dockerfile below followed by docker image history --no-trunc --format "{{json .}}".
// Standard output and standard error were captured separately, exactly as docker printed them.
public static class LayerSourceRecommendationFixtures
{
    public const string HttpdOut = """"


 Target   │  httpd:2.4     
   digest │  c6b7745aa371  

## Recommended fixes

  Base image is  debian:13-slim 

 Name            │  13-slim                                                                  
 Digest          │  sha256:1e1a22175fbf1b32157ddaf0219e1db4e5401fab2b94ed285add64d18587cd2d  
 Vulnerabilities │    0C     3H     0M     9L                                                
 Pushed          │ 1 week ago                                                                
 Size            │ 30 MB                                                                     
 Packages        │ 111                                                                       
 OS              │ 13.7                                                                      

                                                                                        
  | The base image is also available under the supported tag(s) 13.7-slim, trixie-slim. 
  | If                                                                                  
  | you want to display recommendations specifically for a different tag, please re-run 
  | the command using the --tag flag.                                                   



Refresh base image
  Rebuild the image using a newer base image version. Updating this may result in breaking changes.


           Tag            │                  Details                  │    Pushed    │       Vulnerabilities       
──────────────────────────┼───────────────────────────────────────────┼──────────────┼─────────────────────────────
  13-slim                 │ Benefits:                                 │ 14 hours ago │    0C     2H     1M    26L  
 Newer image for same tag │ • Same OS detected                        │              │           -1     +1    +17  
 Also known as:           │ • Newer image for same tag                │              │                             
 • 13.7-slim              │ • Minor OS version update                 │              │                             
 • trixie-slim            │ • Tag was pushed more recently            │              │                             
 • trixie-20261104-slim   │ • Image has similar size                  │              │                             
                          │ • Image contains equal number of packages │              │                             
                          │ • Tag is using slim variant               │              │                             
                          │                                           │              │                             
                          │ Image details:                            │              │                             
                          │ • Size: 30 MB                             │              │                             
                          │ • OS: 13.7                                │              │                             
                          │                                           │              │                             
                          │                                           │              │                             
                          │                                           │              │                             


Change base image
  The list displays new recommended tags in descending order, where the top results are rated as most suitable.


          Tag           │                    Details                    │    Pushed    │       Vulnerabilities       
────────────────────────┼───────────────────────────────────────────────┼──────────────┼─────────────────────────────
  stable-slim           │ Benefits:                                     │ 14 hours ago │    0C     2H     1M    26L  
 Tag is preferred tag   │ • Same OS detected                            │              │           -1     +1    +17  
 Also known as:         │ • Tag is preferred tag                        │              │                             
 • stable-20261104-slim │ • Tag was pushed more recently                │              │                             
                        │ • Image has similar size                      │              │                             
                        │ • Image contains equal number of packages     │              │                             
                        │ • Tag is using slim variant                   │              │                             
                        │ • stable-slim was pulled 46K times last month │              │                             
                        │                                               │              │                             
                        │ Image details:                                │              │                             
                        │ • Size: 30 MB                                 │              │                             
                        │ • Flavor: debian                              │              │                             
                        │ • OS: 12                                      │              │                             
                        │ • Slim: v                                     │              │                             
                        │                                               │              │                             
                        │                                               │              │                             
                        │                                               │              │                             
  13                    │ Benefits:                                     │ 14 hours ago │    0C     2H     1M    26L  
 Tag is latest          │ • Same OS detected                            │              │           -1     +1    +17  
 Also known as:         │ • Minor OS version update                     │              │                             
 • 13.7                 │ • Tag was pushed more recently                │              │                             
 • trixie               │ • Tag is latest                               │              │                             
 • latest               │ • Image contains equal number of packages     │              │                             
 • trixie-20261104      │                                               │              │                             
                        │ Image details:                                │              │                             
                        │ • Size: 49 MB                                 │              │                             
                        │ • OS: 13.7                                    │              │                             
                        │                                               │              │                             
                        │                                               │              │                             
                        │                                               │              │                             

"""";
    public const string HttpdErr = """"
    i New version 1.29.0 available (installed version is 1.22.0) at https://github.com/docker/scout-cli
    ...Storing image for indexing
    v Image stored for indexing
    ...Indexing
    v Indexed 171 packages
    i Base image was auto-detected. To get more accurate recommendations, build images with max-mode provenance attestations.
      Review https://docs.docker.com/build/attestations/slsa-provenance/ for more information.
      Alternatively, use  docker scout recommendations --tag <base image tag>  to pass a specific base image tag.
"""";
    public const string MinecraftOut = """"


 Target   │  itzg/minecraft-server:java21  
   digest │  463d45819976                  

## Recommended fixes

  Base image is  eclipse-temurin:21-jre 

 Name            │  21-jre                                                                   
 Digest          │  sha256:6e994f194cdbcb1372486c2210c7e6381a9b2ad7a813ba96cd7f2c9abec799e9  
 Vulnerabilities │    0C     0H     0M     0L                                                
 Pushed          │ 6 days ago                                                                
 Size            │ 116 MB                                                                    
 Packages        │ 210                                                                       
 Runtime         │ 21.0.13_9                                                                 

                                                                                        
  | The base image is also available under the supported tag(s) 21-jre-resolute, 21.0.  
  | 13_9-                                                                               
  | jre, 21.0.13_9-jre-resolute. If you want to display recommendations specifically for
  | a                                                                                   
  | different tag, please re-run the command using the --tag flag.                      



Refresh base image
  Rebuild the image using a newer base image version. Updating this may result in breaking changes.

  v This image version is up to date.


Change base image
  The list displays new recommended tags in descending order, where the top results are rated as most suitable.


             Tag              │                  Details                   │   Pushed   │       Vulnerabilities       
──────────────────────────────┼────────────────────────────────────────────┼────────────┼─────────────────────────────
  25-jre                      │ Benefits:                                  │ 6 days ago │    0C     0H     0M     0L  
 Major runtime version update │ • Image contains 52 fewer packages         │            │                             
 Also known as:               │ • Major runtime version update             │            │                             
 • 25.0.5_8-jre               │ • Tag was pushed more recently             │            │                             
 • 25-jre-resolute            │ • Image has similar size                   │            │                             
 • 25.0.5_8-jre-resolute      │ • Image has same number of vulnerabilities │            │                             
                              │                                            │            │                             
                              │ Image details:                             │            │                             
                              │ • Size: 117 MB                             │            │                             
                              │ • Runtime: 25.0.5_8                        │            │                             
                              │                                            │            │                             
                              │                                            │            │                             
                              │                                            │            │                             
  27-jre                      │ Benefits:                                  │ 1 week ago │    0C     0H     0M     0L  
 Major runtime version update │ • Image contains 52 fewer packages         │            │                             
 Also known as:               │ • Major runtime version update             │            │                             
 • 27_35-jre                  │ • Image has similar size                   │            │                             
 • 27-jre-resolute            │ • Image has same number of vulnerabilities │            │                             
 • 27_35-jre-resolute         │                                            │            │                             
                              │ Image details:                             │            │                             
                              │ • Size: 118 MB                             │            │                             
                              │ • Runtime: 27                              │            │                             
                              │                                            │            │                             
                              │                                            │            │                             
                              │                                            │            │                             

"""";
    public const string NginxOut = """"


 Target   │  nginx:alpine  
   digest │  785835ea65db  

## Recommended fixes

  Base image is  nginx:1-alpine-slim 

 Name            │  1-alpine-slim                                                            
 Digest          │  sha256:f9238a4aacf18df0f059cf0389490fc2e876352f75d019f475a48e8765576f24  
 Vulnerabilities │    0C     0H     0M     0L     1?                                         
 Pushed          │ 2 weeks ago                                                               
 Size            │ 5.8 MB                                                                    
 Packages        │ 26                                                                        
 Flavor          │ alpine                                                                    
 Runtime         │ 1.31.8                                                                    

                                                                                        
  | The base image is also available under the supported tag(s) 1-alpine3.24-slim, 1.31-
  | alpine-slim, 1.31-alpine3.24-slim, 1.31.8-alpine-slim, 1.31.8-alpine3.24-slim,      
  alpine-                                                                               
  | slim,                                                                               
  | alpine3.24-slim, mainline-alpine-slim, mainline-alpine3.24-slim. If you want to     
  | display                                                                             
  | recommendations specifically for a different tag, please re-run the command using   
  the --                                                                                
  | tag flag.                                                                           



Refresh base image
  Rebuild the image using a newer base image version. Updating this may result in breaking changes.

  v This image version is up to date.


Change base image
  The list displays new recommended tags in descending order, where the top results are rated as most suitable.

  v There are no tag recommendations at this time.

"""";
    public const string DebianOut = """"

Could not display recommendations for  debian:bookworm-slim 
image has no base image
"""";
    public const string PullFailedErr = """"
    i New version 1.29.0 available (installed version is 1.22.0) at https://github.com/docker/scout-cli
    ...Pulling
    x Pull failed
"""";
    public const string ProbeDockerfile = """"
# syntax=docker/dockerfile:1
FROM alpine:3.20 AS deps
WORKDIR /app
RUN echo deps > deps.txt

FROM deps AS build
RUN echo build > build.txt

FROM alpine:3.20
ARG VERSION=1.2.3
ENV APP_VERSION=$VERSION \
    MODE=prod
WORKDIR /app
COPY --from=deps /app/deps.txt ./deps.txt
COPY --from=build --chown=1000:1000 /app/build.txt ./build.txt
COPY --chmod=755 hello.sh /usr/local/bin/hello.sh
ADD note.txt /note.txt
RUN echo "version $VERSION" > /version.txt && \
    echo done
RUN ["sh", "-c", "echo exec > /exec.txt"]
RUN --mount=type=cache,target=/var/cache/apk echo cached > /c.txt
LABEL org.example.name="probe" org.example.kind=test
EXPOSE 8080
USER 1000
HEALTHCHECK --interval=30s CMD wget -qO- localhost:8080 || exit 1
VOLUME ["/data"]
STOPSIGNAL SIGTERM
ENTRYPOINT ["/usr/local/bin/hello.sh"]
CMD node dist/server.js
"""";
    public const string ProbeHistory = """"
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:57-05:00","CreatedBy":"CMD [\"/bin/sh\" \"-c\" \"node dist/server.js\"]","CreatedSince":"2 seconds ago","ID":"sha256:4b5a743355ebaf9ae316b8ce27e738c86717180dcacca7eee5d6ff6fa8a861bb","Size":"0B"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:57-05:00","CreatedBy":"ENTRYPOINT [\"/usr/local/bin/hello.sh\"]","CreatedSince":"2 seconds ago","ID":"\u003cmissing\u003e","Size":"0B"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:57-05:00","CreatedBy":"STOPSIGNAL SIGTERM","CreatedSince":"2 seconds ago","ID":"\u003cmissing\u003e","Size":"0B"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:57-05:00","CreatedBy":"VOLUME [/data]","CreatedSince":"2 seconds ago","ID":"\u003cmissing\u003e","Size":"0B"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:57-05:00","CreatedBy":"HEALTHCHECK {Test:[CMD-SHELL wget -qO- localhost:8080 || exit 1] Interval:30s Timeout:0s StartPeriod:0s StartInterval:0s Retries:0}","CreatedSince":"2 seconds ago","ID":"\u003cmissing\u003e","Size":"0B"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:57-05:00","CreatedBy":"USER 1000","CreatedSince":"2 seconds ago","ID":"\u003cmissing\u003e","Size":"0B"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:57-05:00","CreatedBy":"EXPOSE [8080/tcp]","CreatedSince":"2 seconds ago","ID":"\u003cmissing\u003e","Size":"0B"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:57-05:00","CreatedBy":"LABEL org.example.name=probe org.example.kind=test","CreatedSince":"2 seconds ago","ID":"\u003cmissing\u003e","Size":"0B"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:57-05:00","CreatedBy":"RUN |1 VERSION=1.2.3 /bin/sh -c echo cached \u003e /c.txt # buildkit","CreatedSince":"2 seconds ago","ID":"\u003cmissing\u003e","Size":"8.19kB"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:57-05:00","CreatedBy":"RUN |1 VERSION=1.2.3 sh -c echo exec \u003e /exec.txt # buildkit","CreatedSince":"2 seconds ago","ID":"\u003cmissing\u003e","Size":"8.19kB"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:56-05:00","CreatedBy":"RUN |1 VERSION=1.2.3 /bin/sh -c echo \"version $VERSION\" \u003e /version.txt \u0026\u0026     echo done # buildkit","CreatedSince":"3 seconds ago","ID":"\u003cmissing\u003e","Size":"8.19kB"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:56-05:00","CreatedBy":"ADD note.txt /note.txt # buildkit","CreatedSince":"3 seconds ago","ID":"\u003cmissing\u003e","Size":"8.19kB"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:56-05:00","CreatedBy":"COPY --chmod=755 hello.sh /usr/local/bin/hello.sh # buildkit","CreatedSince":"3 seconds ago","ID":"\u003cmissing\u003e","Size":"20.5kB"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:56-05:00","CreatedBy":"COPY --chown=1000:1000 /app/build.txt ./build.txt # buildkit","CreatedSince":"3 seconds ago","ID":"\u003cmissing\u003e","Size":"12.3kB"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:56-05:00","CreatedBy":"COPY /app/deps.txt ./deps.txt # buildkit","CreatedSince":"3 seconds ago","ID":"\u003cmissing\u003e","Size":"12.3kB"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:55-05:00","CreatedBy":"WORKDIR /app","CreatedSince":"4 seconds ago","ID":"\u003cmissing\u003e","Size":"8.19kB"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:55-05:00","CreatedBy":"ENV APP_VERSION=1.2.3 MODE=prod","CreatedSince":"4 seconds ago","ID":"\u003cmissing\u003e","Size":"0B"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-11-05T06:48:55-05:00","CreatedBy":"ARG VERSION=1.2.3","CreatedSince":"4 seconds ago","ID":"\u003cmissing\u003e","Size":"0B"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-04-16T19:53:26-04:00","CreatedBy":"CMD [\"/bin/sh\"]","CreatedSince":"6 months ago","ID":"\u003cmissing\u003e","Size":"0B"}
{"Comment":"buildkit.dockerfile.v0","CreatedAt":"2026-04-16T19:53:26-04:00","CreatedBy":"ADD alpine-minirootfs-3.20.10-x86_64.tar.gz / # buildkit","CreatedSince":"6 months ago","ID":"\u003cmissing\u003e","Size":"8.47MB"}
"""";
}
