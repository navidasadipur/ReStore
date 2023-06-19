import { Box, Typography } from "@mui/material";
import { Link } from "react-router-dom";
import Slider from "react-slick";

export default function HomePage() {
    const settings = {
        dots: true,
        infinite: true,
        speed: 500,
        slidesToShow: 1,
        slidesToScroll: 1
    };

    return (
        <>
            <Slider {...settings} >
                <div>
                    <img src="/images/hero1.jpg" alt="hero" style={{ display: 'block', width: '100%', maxHeight: 500 }} />
                </div>
                <div>
                    <img src="/images/hero2.jpg" alt="hero" style={{ display: 'block', width: '100%', maxHeight: 500 }} />
                </div>
                <div>
                    <img src="/images/hero3.jpg" alt="hero" style={{ display: 'block', width: '100%', maxHeight: 500 }} />
                </div>
            </Slider>

            <Box display='flex' justifyContent='center' sx={{ p: 4 }}>
                <Typography variant='h3'>
                    Welcome to the my shop!
                </Typography>
            </Box>
            <Box display='flex' justifyContent='center' sx={{ p: 4 }}>
                <Typography variant='h4' display='flex' justifyContent='center' textAlign='center' flexDirection='column' flexWrap="wrap">
                    <p>see all codes of this shop in my GitHub repository:</p>
                    <Link to='https://github.com/navidasadipur/ReStore' >
                        https://github.com/navidasadipur/ReStore
                    </Link>
                </Typography>
            </Box>
            <Box display='flex' justifyContent='center' sx={{ p: 4 }}>
                <Typography variant='h5' display='flex' justifyContent='center' textAlign='center' flexDirection='column' flexWrap="wrap">
                    <p>my other project that is coming soon: </p>
                    <Link to='https://reactivites.navidasadipur.ir/'  >
                        reactivites.navidasadipur.ir
                    </Link>
                    <Link to='https://github.com/navidasadipur/Reactivites' >
                        https://github.com/navidasadipur/Reactivites
                    </Link>
                </Typography>
            </Box>
        </>
    )
}