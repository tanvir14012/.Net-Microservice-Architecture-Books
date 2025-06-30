Start Docker Desktop.

Update WSL in CMD (if not): wsl --update

1. docker pull jenkins/jenkins:lts
2. docker volume create jenkins_data
3. docker run -d --name jenkins -p 8080:8080 -p 50000:50000 -v jenkins_data:/var/jenkins_home jenkins/jenkins:lts

http://localhost:8080
Run this to get the password in CMD: docker exec jenkins cat /var/jenkins_home/secrets/initialAdminPassword
Password: 6707ff509ff74111817f8c283c48daa7

To go to folder of Jenkins root, run in CMD:
1. docker exec -it jenkins bash
2. cd /var/jenkins_home
3. ls -ltra