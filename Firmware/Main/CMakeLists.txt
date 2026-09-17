cmake_minimum_required(VERSION 3.22)

#
# This file is generated only once,
# and is not re-generated if converter is called multiple times.
#
# User is free to modify the file as much as necessary
#

# Setup compiler settings
set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS ON)


# Define the build type
if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE "Debug")
endif()

# Set the project name
set(CMAKE_PROJECT_NAME IF_main)

# Enable compile command to ease indexing with e.g. clangd
set(CMAKE_EXPORT_COMPILE_COMMANDS TRUE)

# Core project settings
project(${CMAKE_PROJECT_NAME})
message("Build type: " ${CMAKE_BUILD_TYPE})

# Enable CMake support for ASM and C languages
enable_language(C ASM)

# Create an executable object type
add_executable(${CMAKE_PROJECT_NAME})

# Add STM32CubeMX generated sources
add_subdirectory(cmake/stm32cubemx)

# Link directories setup
target_link_directories(${CMAKE_PROJECT_NAME} PRIVATE
    # Add user defined library search paths
)

# Add sources to executable
target_sources(${CMAKE_PROJECT_NAME} PRIVATE
    # Add user sources here
    User/Application/Src/a_application.c
    User/Application/Src/a_at25sf041b.c
    User/Application/Src/a_boot_link.c
    User/Application/Src/a_bootloader_link.c
    User/Application/Src/a_comm_config.c
    User/Application/Src/a_desired_state_mirror.c
    User/Application/Src/a_gst_protocol.c
    User/Application/Src/a_log.c
    User/Application/Src/a_modbus_master.c
    User/Application/Src/a_modbus_slave.c
    User/Application/Src/a_plc_group_map.c
    User/Application/Src/a_poll_table.c
    User/Application/Src/a_shared_map.c
    User/Application/Src/a_test_link.c
    User/Application/Src/a_uart.c
    User/Application/Src/bootloader_shared.c
    User/Application/Src/m_modbus.c
    USB_DEVICE/App/usb_device.c
    USB_DEVICE/App/usbd_cdc_if.c
    USB_DEVICE/App/usbd_desc.c
    USB_DEVICE/Target/usbd_conf.c
    Middlewares/ST/STM32_USB_Device_Library/Core/Src/usbd_core.c
    Middlewares/ST/STM32_USB_Device_Library/Core/Src/usbd_ctlreq.c
    Middlewares/ST/STM32_USB_Device_Library/Core/Src/usbd_ioreq.c
    Middlewares/ST/STM32_USB_Device_Library/Class/CDC/Src/usbd_cdc.c
    Drivers/STM32L4xx_HAL_Driver/Src/stm32l4xx_hal_iwdg.c
)

# Add include paths
target_include_directories(${CMAKE_PROJECT_NAME} PRIVATE
    # Add user defined include paths
    User/Application/Inc
    USB_DEVICE/App
    USB_DEVICE/Target
    Middlewares/ST/STM32_USB_Device_Library/Core/Inc
    Middlewares/ST/STM32_USB_Device_Library/Class/CDC/Inc
)

# Add project symbols (macros)
target_compile_definitions(${CMAKE_PROJECT_NAME} PRIVATE
    # Add user defined symbols
)

# Remove wrong libob.a library dependency when using cpp files
list(REMOVE_ITEM CMAKE_C_IMPLICIT_LINK_LIBRARIES ob)

# Add linked libraries
target_link_libraries(${CMAKE_PROJECT_NAME}
    stm32cubemx

    # Add user defined libraries
)
